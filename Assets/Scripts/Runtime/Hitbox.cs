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

    [Tooltip("The part of the body this collider stands for. Wounds, limps, a dropped " +
             "rifle and the way the body falls are all decided from it. A headshot box " +
             "is the head whatever this says.")]
    public BodyPart part = BodyPart.Torso;

    /// <summary>What this hitbox reports, with the headshot flag taking precedence.</summary>
    public BodyPart Part => isHeadshot ? BodyPart.Head : part;

    EnemyWounds _wounds;

    void Reset() => owner = GetComponentInParent<Health>();

    void Awake()
    {
        if (owner == null) owner = GetComponentInParent<Health>();
        if (owner != null) _wounds = owner.GetComponent<EnemyWounds>();
    }

    /// <summary>
    /// Applies the multiplier and forwards. The returned amount is what actually
    /// landed, not what was requested -- a corpse still has ragdoll colliders soaking
    /// raycasts, and an immunity window swallows a hit outright. Reporting the request
    /// instead would hand the shooter a hitmarker and a damage number for a body that
    /// took nothing.
    ///
    /// A headshot on something that cannot survive one is raised to the whole of what
    /// it has left, rather than killed on the side: it has to arrive as damage so the
    /// Damaged and Died events, the kill feed, the score and the stars all see exactly
    /// the hit that did it. Whether it can survive one is <see cref="EnemyWounds"/>'s
    /// call -- a boss cannot be one-shot, and an elite cannot while its armour is up.
    /// </summary>
    public DamageInfo Receive(DamageInfo info)
    {
        info.amount *= damageMultiplier;
        info.isHeadshot = isHeadshot;
        info.part = Part;

        if (owner != null && isHeadshot && info.amount > 0f && _wounds != null
            && _wounds.HeadshotIsLethal)
            info.amount = Mathf.Max(info.amount, owner.Current + owner.Shield + 1f);

        info.amount = owner != null ? owner.ApplyDamage(info) : 0f;

        return info;
    }
}
