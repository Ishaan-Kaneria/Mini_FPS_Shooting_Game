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

    [Tooltip("Seconds a corpse must lie nearly still before it is frozen where it lies.")]
    [Min(0.1f)] public float settleSeconds = 0.6f;

    [Tooltip("Seconds after which a corpse is frozen however it is moving.")]
    [Min(1f)] public float maxSettleSeconds = 7f;

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
        // Killed while already thrown (a kick, say): it only has to be let come to rest.
        if (_settle != null) StopCoroutine(_settle);
        _settle = StartCoroutine(Settle());
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
        if (active && !_ignoringSelf) { IgnoreSelf(); _ignoringSelf = true; }

        foreach (var bone in _bones)
        {
            if (bone == null) continue;

            bone.isKinematic = !active;
            if (active)
            {
                // Steadier joints, and no explosive push-out: Unity's default lets two
                // overlapping parts shove apart at any speed, which reads as a shiver.
                bone.solverIterations = 12;
                bone.solverVelocityIterations = 4;
                bone.maxDepenetrationVelocity = 1.5f;
                bone.sleepThreshold = 0.05f;
            }
            // Collisions stay on either way: alive, the kinematic bones are the body the
            // player bumps into and the rounds hit; dead or thrown, they are the ragdoll.
            bone.detectCollisions = true;

            // Bone colliders stay enabled either way -- they are the hitboxes.
            if (active) bone.WakeUp();
        }
    }

    Coroutine _settle;
    bool _ignoringSelf;

    /// <summary>
    /// The body's parts never collide with each other: only jointed pairs are kept apart by
    /// the joints, and the rest (thigh on thigh, head on chest) sit close enough to overlap
    /// and push at each other for ever.
    /// </summary>
    void IgnoreSelf()
    {
        var own = GetComponentsInChildren<Collider>();
        for (int i = 0; i < own.Length; i++)
            for (int j = i + 1; j < own.Length; j++)
                if (own[i] != null && own[j] != null) Physics.IgnoreCollision(own[i], own[j]);
    }

    /// <summary>
    /// Once it has come to rest, the corpse is frozen where it lies: no solver left running
    /// to twitch it, and nothing to pay for a body that will not move again. A body thrown
    /// by a kick is not dead and is left alone (that is EnemyBody's).
    /// </summary>
    System.Collections.IEnumerator Settle()
    {
        float start = Time.time, stillSince = -1f;
        yield return new WaitForSeconds(0.8f);
        while (IsRagdolled && Time.time - start < maxSettleSeconds)
        {
            bool still = true;
            foreach (var bone in _bones)
            {
                if (bone == null || bone.isKinematic) continue;
                if (bone.linearVelocity.sqrMagnitude > 0.2f * 0.2f || bone.angularVelocity.sqrMagnitude > 0.8f * 0.8f) { still = false; break; }
            }
            if (!still) stillSince = -1f;
            else if (stillSince < 0f) stillSince = Time.time;
            else if (Time.time - stillSince >= settleSeconds) break;
            yield return new WaitForSeconds(0.1f);
        }
        if (!IsRagdolled || (_health != null && !_health.IsDead)) yield break;
        foreach (var bone in _bones)
        {
            if (bone == null || bone.isKinematic) continue;
            bone.linearVelocity = Vector3.zero;
            bone.angularVelocity = Vector3.zero;
            bone.isKinematic = true;
        }
        _settle = null;
    }
}
