using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Swings an enemy's limbs from its own movement, with no AnimationClips and no
/// AnimatorController anywhere in the project.
///
/// The kit builds its enemies out of primitives, so there is no rig to import and
/// nothing to author animation against. Driving the limbs from the NavMeshAgent's
/// velocity costs a few trig calls per enemy and makes the difference between a
/// capsule sliding across the floor and something that reads as walking toward you.
///
/// Everything is expressed as an offset from the pose the builder left the limb in,
/// captured once at <see cref="Awake"/>. That way the archetype is still free to
/// rescale or reposition limbs -- this only ever rotates around whatever it finds.
///
/// <see cref="EnemyAI.CurrentState"/> is the only thing read from the AI, so the two
/// stay independent: an enemy with no limbs assigned simply does nothing here.
/// </summary>
[DisallowMultipleComponent]
public class EnemyLimbAnimator : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Left alone if empty, so a partial rig animates whatever it does have.")]
    public Transform leftArm;
    public Transform rightArm;
    public Transform leftLeg;
    public Transform rightLeg;

    [Tooltip("Bobs and leans. Usually the torso, which carries the head with it.")]
    public Transform torso;

    [Header("Walk")]
    [Tooltip("How many full strides the legs take per metre travelled. Tied to distance " +
             "rather than to time so a slow archetype does not moonwalk.")]
    public float stridesPerMetre = 0.55f;

    [Tooltip("How far a leg swings at full speed, in degrees.")]
    public float strideAngle = 38f;

    [Tooltip("Arms swing opposite the legs, at this fraction of the leg angle.")]
    [Range(0f, 2f)] public float armSwingFactor = 0.8f;

    [Tooltip("Vertical bounce of the torso across a stride, in metres. Two bounces per " +
             "stride, because the body rises on each footfall rather than once per cycle.")]
    public float bobHeight = 0.045f;

    [Tooltip("Degrees the torso leans into the direction of travel at full speed.")]
    public float runLean = 9f;

    [Tooltip("Speed the walk cycle is considered 'full'. Below it the swing scales down, " +
             "so an enemy easing to a stop settles instead of snapping to rest.")]
    public float fullSpeed = 4f;

    [Header("Weapon")]
    [Tooltip("The rifle the arms carry. Switched off automatically on an enemy whose " +
             "archetype is not ranged, so one prefab serves the whole roster.")]
    public GameObject weapon;

    [Tooltip("Degrees an armed enemy holds its arms at while on its feet, so the gun " +
             "points where the enemy is looking instead of at the floor.")]
    public float aimRaise = 76f;

    [Tooltip("How much of the walk swing survives while carrying a weapon. Near zero: " +
             "a braced rifle does not swing with the stride, and a gun waving through " +
             "the full arm arc looks broken rather than lively.")]
    [Range(0f, 1f)] public float armedSwingFactor = 0.12f;

    [Header("Attack")]
    [Tooltip("Degrees the arms rise while winding up and striking.")]
    public float attackRaise = 95f;

    [Tooltip("How fast the attack pose is blended in and out. Deliberately faster in " +
             "than out: the wind-up should snap, the recovery should sag.")]
    public float attackBlendIn = 14f;
    public float attackBlendOut = 5f;

    [Header("Death")]
    [Tooltip("Degrees the torso pitches forward as it dies, when no ragdoll takes over.")]
    public float deathSlump = 70f;
    public float deathSpeed = 4f;

    NavMeshAgent _agent;
    EnemyAI _ai;

    Quaternion _leftArmRest, _rightArmRest, _leftLegRest, _rightLegRest, _torsoRest;
    Vector3 _torsoRestPosition;

    /// <summary>Distance-based, so the cycle is continuous across speed changes.</summary>
    float _stridePhase;
    float _attack01;
    float _death01;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _ai = GetComponent<EnemyAI>();

        // Captured before anything animates, so "rest" is whatever the builder and the
        // archetype agreed on rather than whatever pose frame one happened to catch.
        if (leftArm != null) _leftArmRest = leftArm.localRotation;
        if (rightArm != null) _rightArmRest = rightArm.localRotation;
        if (leftLeg != null) _leftLegRest = leftLeg.localRotation;
        if (rightLeg != null) _rightLegRest = rightLeg.localRotation;

        if (torso != null)
        {
            _torsoRest = torso.localRotation;
            _torsoRestPosition = torso.localPosition;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool dead = _ai != null && _ai.CurrentState == EnemyAI.State.Dead;
        bool attacking = _ai != null && _ai.CurrentState == EnemyAI.State.Attack;

        _death01 = Mathf.MoveTowards(_death01, dead ? 1f : 0f, deathSpeed * dt);

        // A dead enemy stops driving its limbs entirely: the ragdoll, if there is one,
        // owns the body from here and fighting it would look like a seizure.
        if (_death01 >= 1f) return;

        float blend = attacking ? attackBlendIn : attackBlendOut;
        _attack01 = Mathf.MoveTowards(_attack01, attacking ? 1f : 0f, blend * dt);

        float speed = _agent != null ? _agent.velocity.magnitude : 0f;
        float speed01 = fullSpeed <= 0f ? 0f : Mathf.Clamp01(speed / fullSpeed);

        _stridePhase += speed * stridesPerMetre * dt * Mathf.PI * 2f;

        float swing = Mathf.Sin(_stridePhase) * strideAngle * speed01;

        ApplyWeaponVisibility();
        ApplyLimbs(swing);
        ApplyTorso(speed01);
    }

    /// <summary>
    /// A melee archetype should not be holding a rifle it never fires.
    ///
    /// Checked every frame against the object's own state rather than cached in Awake,
    /// because EnemyArchetype stamps `ranged` onto the AI *after* the prefab is
    /// instantiated -- at Awake every enemy still looks unarmed. Comparing against
    /// activeSelf means this costs a bool test and holds no state of its own.
    /// </summary>
    void ApplyWeaponVisibility()
    {
        if (weapon == null) return;

        bool armed = _ai != null && _ai.ranged;
        if (weapon.activeSelf != armed) weapon.SetActive(armed);
    }

    bool Armed => weapon != null && _ai != null && _ai.ranged;

    void ApplyLimbs(float swing)
    {
        // Legs lead, arms counter-swing: the opposite-limb pairing is most of what makes
        // a walk read as a walk rather than a shuffle. Legs are unaffected by what the
        // hands are doing, so they always hold at rest and always swing fully.
        SetLimb(leftLeg, _leftLegRest, swing, 0f, 1f);
        SetLimb(rightLeg, _rightLegRest, -swing, 0f, 1f);

        // An armed enemy already holds its arms up, and the attack pose pulls them the
        // rest of the way -- so the two poses blend instead of fighting.
        float hold = Mathf.Lerp(Armed ? -aimRaise : 0f, -attackRaise, _attack01);

        // Swing fades out as the attack takes over, so a lunging enemy is not also
        // pumping its arms, and a carried rifle barely moves at all.
        float weight = (1f - _attack01) * (Armed ? armedSwingFactor : 1f);

        float armSwing = -swing * armSwingFactor;

        SetLimb(leftArm, _leftArmRest, armSwing, hold, weight);
        SetLimb(rightArm, _rightArmRest, -armSwing, hold, weight);
    }

    void SetLimb(Transform limb, Quaternion rest, float swingDegrees, float holdDegrees,
                 float swingWeight)
    {
        if (limb == null) return;

        float pitch = holdDegrees + swingDegrees * swingWeight;
        limb.localRotation = rest * Quaternion.Euler(pitch, 0f, 0f);
    }

    void ApplyTorso(float speed01)
    {
        if (torso == null) return;

        // Two bounces per stride: the body rises on each footfall, not once per cycle.
        float bob = Mathf.Abs(Mathf.Sin(_stridePhase)) * bobHeight * speed01;
        torso.localPosition = _torsoRestPosition + Vector3.up * bob;

        float lean = runLean * speed01;

        // The attack leans further in and death folds the whole thing forward, so the
        // three poses share one axis instead of fighting over the transform.
        lean = Mathf.Lerp(lean, runLean + 12f, _attack01);
        lean = Mathf.Lerp(lean, deathSlump, _death01);

        torso.localRotation = _torsoRest * Quaternion.Euler(lean, 0f, 0f);
    }
}
