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

    /// <summary>The death clips on the "Injured" layer (FPSKitRiggedSoldier), by what killed it.</summary>
    public static readonly string[] DeathStates =
    {
        "Death_FallBack", "Death_Back", "Death_FallForward", "Death_Front",
        "Death_Back_Head", "Death_Front_Head", "Death_Head_TwoKnees",
    };

    /// <summary>A death clip is playing and the ragdoll takes over at the end of it.</summary>
    public bool Dying => _dyingUntil > 0f;

    float _dyingUntil = -1f;
    Vector3[] _lastPos;
    Quaternion[] _lastRot;
    bool _haveLast;

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
        // An ordinary killing round: the start of the fall is a clip, the rest is the ragdoll.
        if (!IsRagdolled && !Dying && StartDeathClip()) return;

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

    // ======================================================================
    // Dying by a clip, then the ragdoll (Stage 3)
    // ======================================================================

    /// <summary>
    /// Plays the start of the fall as a clip (by where the round came from: shot from the front it
    /// falls back, from behind forward, through the head its own; through a leg it goes to its
    /// knees), and at the end of it hands the bones to physics carrying the clip's motion, so the
    /// body falls on without a jolt. Blasts, kicks, punches, heavy blows and a body on the ground
    /// go straight to the ragdoll (false).
    /// </summary>
    bool StartDeathClip()
    {
        // The killing blow: the Damaged event that records it comes after Died, so read the health's own.
        if (_health != null) { _lastHit = _health.LastDamage; _hasLastHit = true; }
        if (animator == null || !animator.enabled || !_hasLastHit) return false;
        var wounds = GetComponent<EnemyWounds>();
        var profile = wounds != null ? wounds.Body : null;
        if (profile == null || profile.deathClipSeconds.y <= 0f) return false;
        var body = GetComponent<EnemyBody>();
        if (body != null && body.IsDown) return false;
        int layer = animator.GetLayerIndex("Injured");
        if (layer < 0) return false;

        float max = _health != null ? Mathf.Max(1f, _health.maxHealth) : 100f;
        float burst = wounds != null ? Mathf.Max(wounds.RecentDamage, _lastHit.amount) : _lastHit.amount;
        bool crawling = wounds != null && wounds.Legs == EnemyWounds.LegState.Crawling;
        var style = EnemyDeath.Classify(_lastHit, burst / max, crawling);
        // Straight to the ragdoll: a blast, a fist or a boot, and a heavy round (shotgun, DMR) blowing it
        // back. A burst of ordinary rounds also reads as "blown back" by its total, but falls by a clip.
        if (crawling || style == EnemyDeath.Style.Thrown || style == EnemyDeath.Style.Spun
            || (style == EnemyDeath.Style.BlownBack && _lastHit.heavy)) return false;

        bool fromBehind = _lastHit.direction.sqrMagnitude > 0.001f && Vector3.Dot(_lastHit.direction, transform.forward) > 0f;
        string state = style == EnemyDeath.Style.Drop
            ? (fromBehind ? "Death_Front_Head" : "Death_Back_Head")
            : style == EnemyDeath.Style.Buckle ? "Death_Head_TwoKnees"
            : fromBehind ? (Random.value < 0.5f ? "Death_FallForward" : "Death_Front")
                         : (Random.value < 0.5f ? "Death_FallBack" : "Death_Back");
        if (!animator.HasState(layer, Animator.StringToHash(state))) return false;

        // Nothing over it: the full body is the clip's (the carry and the sidearm layers are the arms').
        for (int i = 0; i < animator.layerCount; i++) if (i != 0 && i != layer) animator.SetLayerWeight(i, 0f);
        animator.SetLayerWeight(layer, 1f);
        animator.CrossFadeInFixedTime(state, 0.06f, layer);
        _dyingUntil = Time.time + Random.Range(profile.deathClipSeconds.x, Mathf.Max(profile.deathClipSeconds.x, profile.deathClipSeconds.y));
        _haveLast = false;
        return true;
    }

    /// <summary>After the Animator: remembers where the bones were, and at the end of the clip gives each its motion as the ragdoll takes it.</summary>
    void LateUpdate()
    {
        if (!Dying || IsRagdolled) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        if (_lastPos == null || _lastPos.Length != _bones.Length) { _lastPos = new Vector3[_bones.Length]; _lastRot = new Quaternion[_bones.Length]; }

        if (Time.time >= _dyingUntil && _haveLast)
        {
            var velocity = new Vector3[_bones.Length];
            var spin = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = _bones[i];
                if (b == null) continue;
                velocity[i] = (b.position - _lastPos[i]) / dt;
                (b.rotation * Quaternion.Inverse(_lastRot[i])).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                spin[i] = axis.sqrMagnitude > 0.5f ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            }
            _dyingUntil = -1f;
            SetRagdollActive(true);
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = _bones[i];
                if (b == null || b.isKinematic) continue;
                b.linearVelocity = Vector3.ClampMagnitude(velocity[i], 6f);
                b.angularVelocity = Vector3.ClampMagnitude(spin[i], 12f);
            }
            if (_settle != null) StopCoroutine(_settle);
            _settle = StartCoroutine(Settle());
            return;
        }

        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            _lastPos[i] = _bones[i].position;
            _lastRot[i] = _bones[i].rotation;
        }
        _haveLast = true;
    }
}
