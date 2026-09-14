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

    /// <summary>
    /// Applies the multiplier and forwards. The returned amount is what actually
    /// landed, not what was requested -- a corpse still has ragdoll colliders soaking
    /// raycasts, and an immunity window swallows a hit outright. Reporting the request
    /// instead would hand the shooter a hitmarker and a damage number for a body that
    /// took nothing.
    /// </summary>
    public DamageInfo Receive(DamageInfo info)
    {
        info.amount *= damageMultiplier;
        info.isHeadshot = isHeadshot;
        info.amount = owner != null ? owner.ApplyDamage(info) : 0f;

        return info;
    }
}
