using UnityEngine;

/// <summary>
/// Holds a rigged character in animated mode until it dies, then hands the bones
/// over to physics. Built by FPSKit > Enemy Setup, but works on any hierarchy that
/// has Rigidbodies on its bones.
/// </summary>
public class RagdollController : MonoBehaviour
{
    [Tooltip("Animator disabled the moment the ragdoll takes over.")]
    public Animator animator;

    [Tooltip("Collider on the root that drives movement while alive. Disabled on death.")]
    public Collider mainCollider;

    [Tooltip("Push applied at the point that landed the killing blow.")]
    public float deathImpulse = 6f;

    public bool IsRagdolled { get; private set; }

    Rigidbody[] _bones;
    Health _health;
    DamageInfo _lastHit;
    bool _hasLastHit;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();

        _bones = GetComponentsInChildren<Rigidbody>();
        _health = GetComponent<Health>();

        SetRagdollActive(false);
    }

    void OnEnable()
    {
        if (_health == null) return;

        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
    }

    void OnDisable()
    {
        if (_health == null) return;

        _health.Damaged -= OnDamaged;
        _health.Died -= OnDied;
    }

    void OnDamaged(Health health, DamageInfo info)
    {
        _lastHit = info;
        _hasLastHit = true;
    }

    void OnDied(Health health) => Collapse();

    /// <summary>Drops the character into physics, pushed by whatever killed it.</summary>
    public void Collapse()
    {
        if (IsRagdolled) return;

        SetRagdollActive(true);

        if (!_hasLastHit || deathImpulse <= 0f) return;

        // The same rules the built enemy falls by (see EnemyDeath): a headshot drops it,
        // a blast throws the whole body, a heavy hit blows it back. deathImpulse scales
        // the lot, so an imported character tuned before these existed keeps its feel.
        var wounds = GetComponent<EnemyWounds>();
        float max = _health != null ? Mathf.Max(1f, _health.maxHealth) : 100f;
        float burst = wounds != null ? Mathf.Max(wounds.RecentDamage, _lastHit.amount) : _lastHit.amount;
        bool crawling = wounds != null && wounds.Legs == EnemyWounds.LegState.Crawling;

        var style = EnemyDeath.Classify(_lastHit, burst / max, crawling);
        Vector3 impulse = EnemyDeath.Blow(_lastHit, style, burst / max) * (deathImpulse / 6f);

        if (style == EnemyDeath.Style.Thrown)
        {
            float total = 0f;
            foreach (var bone in _bones) if (bone != null) total += bone.mass;

            foreach (var bone in _bones)
                if (bone != null) bone.AddForce(impulse * (bone.mass / Mathf.Max(0.01f, total)), ForceMode.Impulse);

            return;
        }

        // Shove the bone nearest the killing shot so the fall reads as a reaction.
        Rigidbody nearest = null;
        float best = float.MaxValue;

        foreach (var bone in _bones)
        {
            // Guarded the same way SetRagdollActive guards it. The array is captured
            // once in Awake, so anything that removes a bone afterwards leaves a hole
            // here -- and reading worldCenterOfMass through one throws.
            if (bone == null) continue;

            float distance = (bone.worldCenterOfMass - _lastHit.point).sqrMagnitude;
            if (distance >= best) continue;

            best = distance;
            nearest = bone;
        }

        if (nearest != null)
            nearest.AddForceAtPosition(impulse, _lastHit.point, ForceMode.Impulse);
    }

    public void SetRagdollActive(bool active)
    {
        IsRagdolled = active;

        if (animator != null) animator.enabled = !active;
        if (mainCollider != null) mainCollider.enabled = !active;

        foreach (var bone in _bones)
        {
            if (bone == null) continue;

            bone.isKinematic = !active;
            bone.detectCollisions = active;

            // Bone colliders stay enabled either way -- they are the hitboxes.
            if (active) bone.WakeUp();
        }
    }
}
