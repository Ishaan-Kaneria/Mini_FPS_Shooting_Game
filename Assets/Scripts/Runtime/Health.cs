using System;
using UnityEngine;

/// <summary>
/// Everything a damage event carries. Passed by value so callers can safely
/// mutate a copy (hitboxes multiply the amount before forwarding it on).
/// </summary>
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
/// Hit points for anything that can be shot. Optional regeneration after a
/// quiet period, optional self-destruct on death.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("Pool")]
    public float maxHealth = 100f;

    [Header("Regeneration")]
    public bool regenerates;
    public float regenDelay = 5f;
    public float regenPerSecond = 15f;

    [Header("Death")]
    public bool destroyOnDeath = true;
    public float destroyDelay;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Normalized => maxHealth <= 0f ? 0f : Mathf.Clamp01(Current / maxHealth);
    public bool IsRegenerating => regenerates && !IsDead && Current < maxHealth &&
                                  Time.time - _lastDamageTime >= regenDelay;

    /// <summary>Raised before death is evaluated. Second arg is the post-multiplier hit.</summary>
    public event Action<Health, DamageInfo> Damaged;
    public event Action<Health> Died;
    public event Action<Health> Changed;

    float _lastDamageTime = -9999f;

    void Awake() => Current = maxHealth;

    void Update()
    {
        if (!IsRegenerating) return;
        Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);
        Changed?.Invoke(this);
    }

    public void ApplyDamage(DamageInfo info)
    {
        if (IsDead || info.amount <= 0f) return;

        Current -= info.amount;
        _lastDamageTime = Time.time;

        Damaged?.Invoke(this, info);
        Changed?.Invoke(this);

        if (Current <= 0f) Kill(info);
    }

    /// <summary>Convenience for melee / environmental damage with no hit geometry.</summary>
    public void ApplyDamage(float amount, GameObject source = null)
        => ApplyDamage(new DamageInfo(amount, transform.position, Vector3.up, Vector3.zero, source));

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        Changed?.Invoke(this);
    }

    public void Kill(DamageInfo info = default)
    {
        if (IsDead) return;

        IsDead = true;
        Current = 0f;
        Changed?.Invoke(this);
        Died?.Invoke(this);

        if (destroyOnDeath) Destroy(gameObject, destroyDelay);
    }

    /// <summary>
    /// Refills to maxHealth. The WaveManager calls this after bumping maxHealth
    /// on a freshly spawned enemy, since Awake has already run by then.
    /// </summary>
    public void ResetHealth()
    {
        IsDead = false;
        Current = maxHealth;
        _lastDamageTime = -9999f;
        Changed?.Invoke(this);
    }
}
