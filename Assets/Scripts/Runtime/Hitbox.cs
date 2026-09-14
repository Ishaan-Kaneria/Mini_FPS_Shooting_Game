using UnityEngine;

/// <summary>
/// Sits on a child collider and routes hits to the owning Health with a
/// damage multiplier. This is what turns a raycast into a headshot.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    public Health owner;
    [Min(0f)] public float damageMultiplier = 1f;
    public bool isHeadshot;

    void Reset() => owner = GetComponentInParent<Health>();

    void Awake()
    {
        if (owner == null) owner = GetComponentInParent<Health>();
    }

    /// <summary>Applies the multiplier and forwards. Returns the resolved hit.</summary>
    public DamageInfo Receive(DamageInfo info)
    {
        info.amount *= damageMultiplier;
        info.isHeadshot = isHeadshot;

        if (owner != null && !owner.IsDead) owner.ApplyDamage(info);
        return info;
    }
}
