using System;
using UnityEngine;

/// <summary>
/// Everything a damage event carries. Passed by value so callers can safely
/// mutate a copy (hitboxes multiply the amount before forwarding it on).
///
/// Serializable so that a field holding one survives a mid-play domain reload.
/// Without the attribute Unity's reload backup drops it while the plain bool
/// beside it survives, which is the asymmetry CLAUDE.md warns about:
/// RagdollController kept _hasLastHit and lost _lastHit, then shoved the bone
/// nearest the world origin with a force of nothing.
/// </summary>
[Serializable]
public struct DamageInfo
{
    public float amount;
    public Vector3 point;
    public Vector3 normal;
    public Vector3 direction;
    public GameObject source;
    public bool isHeadshot;

    public DamageInfo(float amount, Vector3 point, Vector3 normal, Vector3 direction, GameObject source)
    {
        this.amount = amount;
        this.point = point;
        this.normal = normal;
        this.direction = direction;
        this.source = source;
        this.isHeadshot = false;
    }
}

/// <summary>
/// Hit points for anything that can be shot, in two layers: a shield that soaks
/// damage first and comes back quickly, and health underneath that comes back
/// slowly or not at all.
///
/// The two-layer split is what makes a fight readable. Shields let the player
/// trade a mistake for positioning instead of a run, and on an enemy they read
/// as armour you have to break before the kill starts counting.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("Pool")]
    public float maxHealth = 100f;

    [Header("Shield")]
    [Tooltip("Soaks damage before health does. Leave at 0 for a plain health bar. " +
             "On the player this is the recoverable layer; on an elite it is armour.")]
    public float maxShield;

    [Tooltip("Quiet seconds before the shield starts refilling. Keep this shorter " +
             "than the health delay -- the shield is the layer you are meant to get back.")]
    public float shieldRegenDelay = 3.5f;
    public float shieldRegenPerSecond = 24f;

    [Header("Regeneration")]
    public bool regenerates;
    public float regenDelay = 5f;
    public float regenPerSecond = 15f;

    [Header("Damage Rules")]
    [Tooltip("Seconds of immunity after a hit lands. A crowd of melee enemies would " +
             "otherwise chain-hit you to death in one second with no window to react. " +
             "Leave at 0 on enemies so shooting fast is never wasted.")]
    [Min(0f)] public float invulnerabilityWindow;

    [Header("Death")]
    public bool destroyOnDeath = true;
    public float destroyDelay;

    public float Current { get; private set; }
    public float Shield { get; private set; }
    public bool IsDead { get; private set; }

    public float Normalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(Current / maxHealth);
    public float ShieldNormalized => maxShield <= 0f ? 0f : Mathf.Clamp01(Shield / maxShield);

    /// <summary>Health plus shield over the same total. What a single combined bar should show.</summary>
    public float TotalNormalized
    {
        get
        {
            float total = maxHealth + maxShield;
            return total <= 0f ? 0f : Mathf.Clamp01((Current + Shield) / total);
        }
    }

    public bool IsFull => Current >= maxHealth && Shield >= maxShield;
    public float LastDamageTime { get; private set; } = -9999f;

    /// <summary>
    /// The hit that landed most recently. Read it from a Died handler to find out
    /// what actually finished this thing off -- headshot, source, direction.
    /// </summary>
    public DamageInfo LastDamage { get; private set; }

    public bool IsInvulnerable => invulnerabilityWindow > 0f &&
                                  Time.time - LastDamageTime < invulnerabilityWindow;

    /// <summary>Raised before death is evaluated. Second arg is the post-multiplier hit.</summary>
    public event Action<Health, DamageInfo> Damaged;
    public event Action<Health> Died;
    public event Action<Health> Changed;

    /// <summary>Fires the frame the shield hits zero. Worth a distinct sound and flash.</summary>
    public event Action<Health> ShieldBroken;

    public event Action<Health, float> Healed;

    void Awake() => ResetHealth();

    void Update()
    {
        if (IsDead) return;

        bool changed = false;
        float quiet = Time.time - LastDamageTime;

        if (maxShield > 0f && Shield < maxShield && quiet >= shieldRegenDelay)
        {
            Shield = Mathf.Min(maxShield, Shield + shieldRegenPerSecond * Time.deltaTime);
            changed = true;
        }

        if (regenerates && Current < maxHealth && quiet >= regenDelay)
        {
            Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);
            changed = true;
        }

        if (changed) Changed?.Invoke(this);
    }

    /// <summary>
    /// Applies a hit across shield then health. Returns the damage dealt -- the full
    /// amount when it lands (a shield absorbs damage, it does not reduce it) and zero
    /// when immunity or death swallowed the hit entirely.
    /// </summary>
    public float ApplyDamage(DamageInfo info)
    {
        if (IsDead || info.amount <= 0f || IsInvulnerable) return 0f;

        float remaining = info.amount;
        bool hadShield = Shield > 0f;

        if (Shield > 0f)
        {
            float absorbed = Mathf.Min(Shield, remaining);
            Shield -= absorbed;
            remaining -= absorbed;
        }

        Current -= remaining;

        LastDamageTime = Time.time;
        LastDamage = info;

        if (hadShield && Shield <= 0f) ShieldBroken?.Invoke(this);

        Damaged?.Invoke(this, info);
        Changed?.Invoke(this);

        if (Current <= 0f) Kill(info);
        return info.amount;
    }

    /// <summary>Convenience for melee / environmental damage with no hit geometry.</summary>
    public float ApplyDamage(float amount, GameObject source = null)
        => ApplyDamage(new DamageInfo(amount, transform.position, Vector3.up, Vector3.zero, source));

    /// <summary>Returns the health actually restored, so a pickup can refuse to be wasted.</summary>
    public float Heal(float amount)
    {
        if (IsDead || amount <= 0f) return 0f;

        float before = Current;
        Current = Mathf.Min(maxHealth, Current + amount);

        float gained = Current - before;
        if (gained <= 0f) return 0f;

        Changed?.Invoke(this);
        Healed?.Invoke(this, gained);
        return gained;
    }

    /// <summary>Tops the shield straight back up, bypassing the regen delay.</summary>
    public float RestoreShield(float amount)
    {
        if (IsDead || amount <= 0f || maxShield <= 0f) return 0f;

        float before = Shield;
        Shield = Mathf.Min(maxShield, Shield + amount);

        float gained = Shield - before;
        if (gained > 0f) Changed?.Invoke(this);
        return gained;
    }

    public void Kill(DamageInfo info = default)
    {
        if (IsDead) return;

        IsDead = true;
        Current = 0f;
        Shield = 0f;

        if (info.amount > 0f) LastDamage = info;

        Changed?.Invoke(this);
        Died?.Invoke(this);

        if (destroyOnDeath) Destroy(gameObject, destroyDelay);
    }

    /// <summary>
    /// Resizes the pool and optionally refills it. The LevelManager uses this to make
    /// later levels tougher: Awake has already run on a freshly spawned enemy, so
    /// raising maxHealth alone would leave it sitting at the old value.
    /// </summary>
    public void SetMaxHealth(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(1f, value);
        if (refill) Current = maxHealth;
        else Current = Mathf.Min(Current, maxHealth);

        Changed?.Invoke(this);
    }

    public void SetMaxShield(float value, bool refill = true)
    {
        maxShield = Mathf.Max(0f, value);
        if (refill) Shield = maxShield;
        else Shield = Mathf.Min(Shield, maxShield);

        Changed?.Invoke(this);
    }

    /// <summary>Back to full, alive, with the damage clock cleared.</summary>
    public void ResetHealth()
    {
        IsDead = false;
        Current = maxHealth;
        Shield = maxShield;
        LastDamageTime = -9999f;
        Changed?.Invoke(this);
    }
}
