using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Poses an enemy's limbs from its own movement and state, with no AnimationClips and
/// no AnimatorController anywhere in the project.
///
/// The kit builds its enemies out of primitives, so there is no rig to import and
/// nothing to author animation against. Driving the limbs from the NavMeshAgent's
/// velocity costs a few trig calls per enemy and makes the difference between a
/// capsule sliding across the floor and something that reads as walking toward you.
///
/// What it draws, in the order it is layered:
///
/// - **A walk** with knees: the thigh swings, the knee folds as the leg comes through
///   and straightens as the foot lands, the body rises twice a stride.
/// - **A rifle held on the target.** The arms hold the gun level whatever the body is
///   leaning, tip it up or down at the player, and kick with each round.
/// - **Wounds** (from <see cref="EnemyWounds"/>). A limp on the hurt leg -- short stride,
///   stiff knee, the body dropping onto it. A wrecked arm hanging. On the floor, a crawl:
///   the body flat, the legs dragging, the arms pulling it along, or holding the rifle
///   out in front if it still has one.
/// - **Hits**, where they land: a leg shot buckles that knee, an arm shot throws that arm,
///   a headshot snaps the head back, a body shot throws the torso along the round.
///
/// Everything is an offset from the pose the builder left each limb in, captured once
/// at <see cref="Awake"/>, so the archetype is still free to rescale the body -- this
/// only ever rotates and lowers what it finds.
///
/// It reads the AI and the wounds as plain values each frame and pushes nothing back,
/// so an enemy with no limbs assigned does nothing here, and a rigged character driven
/// by a real Animator can ignore the lot without either of them needing a second path.
/// Once the enemy is dead it stops: <see cref="EnemyDeath"/> owns the body from there.
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

    [Tooltip("Elbows and knees: the pivot the lower half of each limb hangs from. " +
             "Empty on an older prefab, which then walks stiff-legged as it always did.")]
    public Transform leftForearm;
    public Transform rightForearm;
    public Transform leftShin;
    public Transform rightShin;

    [Tooltip("The neck pivot. Turns the head toward the player and snaps it back on a headshot.")]
    public Transform head;

    [Header("Walk")]
    [Tooltip("How many full strides the legs take per metre travelled. Tied to distance " +
             "rather than to time so a slow archetype does not moonwalk.")]
    public float stridesPerMetre = 0.55f;

    [Tooltip("How far a leg swings at full speed, in degrees.")]
    public float strideAngle = 34f;

    [Tooltip("How far the knee folds as the leg swings through, in degrees.")]
    public float kneeBend = 55f;

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

    [Tooltip("Where each hand holds the rifle: the pistol grip and the handguard. With " +
             "both set, the rifle is aimed on its own and the arms reach for these with " +
             "the elbows bent. Empty on an older prefab, whose arms carry the rifle held " +
             "out straight.")]
    public Transform rightGrip;
    public Transform leftGrip;

    [Tooltip("Shoulder to the middle of the hand, below the elbow, in metres at scale 1.")]
    public float handReach = 0.31f;

    [Tooltip("Where the rifle is held while crawling, relative to the hips with the body " +
             "level: out in front of the face, near the floor.")]
    public Vector3 proneWeaponPosition = new Vector3(0.07f, 0.1f, 0.78f);

    [Tooltip("Furthest the arms tip the rifle up or down at the player, in degrees.")]
    public float maxAimPitch = 30f;

    [Tooltip("Degrees the rifle kicks up with each round.")]
    public float recoilKick = 7f;

    [Header("Attack")]
    [Tooltip("Degrees the arms rise while winding up and striking.")]
    public float attackRaise = 95f;

    [Tooltip("How fast the attack pose is blended in and out. Deliberately faster in " +
             "than out: the wind-up should snap, the recovery should sag.")]
    public float attackBlendIn = 14f;
    public float attackBlendOut = 5f;

    [Header("Hit Reaction")]
    [Tooltip("Degrees the torso is thrown by a hit at full strength. This is the whole " +
             "reason a bullet reads as landing rather than as a number going down.")]
    public float hitThrow = 26f;

    [Tooltip("How far the arms are thrown open by the same hit, as a fraction of the " +
             "torso's throw. A body that is hit loses its guard first.")]
    [Range(0f, 2f)] public float hitArmThrow = 0.7f;

    [Tooltip("Metres the body is shoved along the bullet at full strength. Small -- the " +
             "feet do not move, so anything large pulls the body off its own legs.")]
    public float hitShove = 0.055f;

    [Tooltip("Seconds the reaction takes to snap out and settle back. The snap is the " +
             "first tenth of it; the rest is the recovery.")]
    [Min(0.05f)] public float hitRecovery = 0.42f;

    [Header("Wounds")]
    [Tooltip("How far the body drops onto a limping leg with each step, in metres.")]
    public float limpDip = 0.07f;

    [Tooltip("Height of the hips above the floor while crawling, in metres.")]
    public float crawlHeight = 0.17f;

    [Tooltip("Degrees the body pitches forward to lie flat while crawling.")]
    public float crawlPitch = 84f;

    [Tooltip("How fast it goes down to the floor, in poses per second.")]
    public float crawlBlend = 1.8f;

    [Header("Death")]
    [Tooltip("Degrees the torso pitches forward as it dies, when nothing else takes the body.")]
    public float deathSlump = 70f;
    public float deathSpeed = 4f;

    NavMeshAgent _agent;
    EnemyAI _ai;
    EnemyWounds _wounds;
    bool _ownedAtDeath;

    Quaternion _leftArmRest, _rightArmRest, _leftLegRest, _rightLegRest, _torsoRest;
    Quaternion _leftForeRest, _rightForeRest, _leftShinRest, _rightShinRest, _headRest;
    Vector3 _torsoRestPosition, _leftLegRestPosition, _rightLegRestPosition;

    /// <summary>Distance-based, so the cycle is continuous across speed changes.</summary>
    float _stridePhase;
    float _attack01;
    float _death01;
    float _crawl01;
    float _aimPitch;

    /// <summary>Strength of the hit reaction this frame, which way it threw the body, and where it landed.</summary>
    float _hit01;
    Vector3 _hitLocal;
    BodyPart _hitPart;

    bool _dropped;

    Vector3 _weaponRestPosition;
    Quaternion _weaponRestRotation;
    float _recoil;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _ai = GetComponent<EnemyAI>();
        _wounds = GetComponent<EnemyWounds>();

        // Something else takes the body at death; this just stops.
        _ownedAtDeath = GetComponent<EnemyDeath>() != null || GetComponent<RagdollController>() != null;

        // Captured before anything animates, so "rest" is whatever the builder and the
        // archetype agreed on rather than whatever pose frame one happened to catch.
        _leftArmRest = Rest(leftArm);
        _rightArmRest = Rest(rightArm);
        _leftLegRest = Rest(leftLeg);
        _rightLegRest = Rest(rightLeg);
        _leftForeRest = Rest(leftForearm);
        _rightForeRest = Rest(rightForearm);
        _leftShinRest = Rest(leftShin);
        _rightShinRest = Rest(rightShin);
        _headRest = Rest(head);

        if (torso != null)
        {
            _torsoRest = torso.localRotation;
            _torsoRestPosition = torso.localPosition;
        }

        if (weapon != null)
        {
            _weaponRestPosition = weapon.transform.localPosition;
            _weaponRestRotation = weapon.transform.localRotation;
        }

        if (leftLeg != null) _leftLegRestPosition = leftLeg.localPosition;
        if (rightLeg != null) _rightLegRestPosition = rightLeg.localPosition;
    }

    static Quaternion Rest(Transform t) => t != null ? t.localRotation : Quaternion.identity;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool dead = _ai != null && _ai.CurrentState == EnemyAI.State.Dead;

        if (dead && _ownedAtDeath) return;

        bool attacking = _ai != null && _ai.CurrentState == EnemyAI.State.Attack;

        _death01 = Mathf.MoveTowards(_death01, dead ? 1f : 0f, deathSpeed * dt);

        // A dead enemy stops driving its limbs entirely once it has slumped.
        if (_death01 >= 1f) return;

        float blend = attacking ? attackBlendIn : attackBlendOut;
        _attack01 = Mathf.MoveTowards(_attack01, attacking ? 1f : 0f, blend * dt);

        bool crawling = _wounds != null && _wounds.Legs == EnemyWounds.LegState.Crawling;
        _crawl01 = Mathf.MoveTowards(_crawl01, crawling ? 1f : 0f, crawlBlend * dt);

        float speed = _agent != null && _agent.enabled ? _agent.velocity.magnitude : 0f;
        float speed01 = fullSpeed <= 0f ? 0f : Mathf.Clamp01(speed / fullSpeed);

        // A crawl is short, heaving pulls: more cycles per metre than a stride.
        float cyclesPerMetre = Mathf.Lerp(stridesPerMetre, stridesPerMetre * 2.2f, _crawl01);
        _stridePhase += speed * cyclesPerMetre * dt * Mathf.PI * 2f;

        ReadHitReaction();
        UpdateAim(dt);

        ApplyWeaponVisibility();

        float torsoPitch = ApplyTorso(speed01);
        ApplyLegs(speed01);
        ApplyArms(speed01, torsoPitch);

        // Aiming the rifle and bringing the hands to it overrides the arm pose above,
        // but only while there is a rifle and grips to bring them to.
        if (Armed && rightGrip != null && leftGrip != null)
        {
            PoseWeapon(torsoPitch);
            ReachFor(rightArm, rightForearm, _rightForeRest, rightGrip.position, 1f);
            ReachFor(leftArm, leftForearm, _leftForeRest, leftGrip.position, -1f);
        }

        ApplyHead(torsoPitch);
    }

    // ======================================================================
    // Inputs
    // ======================================================================

    /// <summary>
    /// Turns the last hit the AI recorded into a curve this frame can pose against.
    ///
    /// Read from the AI every frame rather than pushed in by an event, for the reason
    /// the whole class exists: the AI does not know whether anything is rigged, and a
    /// custom enemy with a real Animator should be able to ignore all of this without
    /// the AI needing a second code path. It is also what keeps the reaction correct
    /// across a mid-play recompile -- three floats and a Vector3 on the AI survive the
    /// domain reload that would take an event subscription with it.
    ///
    /// The shape is a fast snap and a slow settle, not a symmetric bump: a body hit by
    /// a bullet moves in one frame and takes a moment to come back.
    /// </summary>
    void ReadHitReaction()
    {
        if (_ai == null)
        {
            _hit01 = 0f;
            return;
        }

        float age = Time.time - _ai.LastReactionTime;
        if (age < 0f || age >= hitRecovery)
        {
            _hit01 = 0f;
            return;
        }

        const float Snap = 0.12f;

        float t = age / hitRecovery;
        float shape = t < Snap ? t / Snap : 1f - (t - Snap) / (1f - Snap);

        // Squared on the way out so the body lingers near the pose and then settles,
        // rather than sliding back at a constant rate.
        _hit01 = _ai.LastReactionStrength * shape * shape;
        _hitLocal = _ai.LastReactionLocal;

        // Which part it was, when the wounds recorded the same hit.
        _hitPart = _wounds != null && Mathf.Abs(_wounds.LastHitTime - _ai.LastReactionTime) < 0.02f
            ? _wounds.LastHitPart
            : BodyPart.Torso;
    }

    /// <summary>How far above or below the level the player is, eased so the rifle tracks rather than snaps.</summary>
    void UpdateAim(float dt)
    {
        float pitch = 0f;

        if (_ai != null && _ai.target != null)
        {
            Vector3 from = _ai.eyes != null ? _ai.eyes.position : transform.position + Vector3.up * 1.6f;
            Vector3 to = _ai.target.position + Vector3.up * 1.2f - from;
            float flat = new Vector2(to.x, to.z).magnitude;

            pitch = Mathf.Clamp(Mathf.Atan2(to.y, Mathf.Max(0.1f, flat)) * Mathf.Rad2Deg,
                                -maxAimPitch, maxAimPitch);
        }

        _aimPitch = Mathf.Lerp(_aimPitch, pitch, 1f - Mathf.Exp(-6f * dt));
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

        bool armed = !_dropped && _ai != null && _ai.ranged;
        if (weapon.activeSelf != armed) weapon.SetActive(armed);
    }

    bool Armed => weapon != null && !_dropped && _ai != null && _ai.ranged;

    // ======================================================================
    // Torso
    // ======================================================================

    /// <summary>Poses the torso and returns the forward pitch it ended up at, which the arms hold the rifle against.</summary>
    float ApplyTorso(float speed01)
    {
        if (torso == null) return 0f;

        // Two bounces per stride: the body rises on each footfall, not once per cycle.
        float bob = Mathf.Abs(Mathf.Sin(_stridePhase)) * bobHeight * speed01 * (1f - _crawl01);

        // A limp drops the body onto the bad leg each time it takes the weight, and rolls
        // it that way. The dip is what the eye reads as a limp from across the arena.
        float limp = LimpWeight;
        float side = BadLegSide;
        float stance = Mathf.Max(0f, -Mathf.Cos(_stridePhase + (side < 0f ? 0f : Mathf.PI)));
        float dip = limp * limpDip * stance * Mathf.Max(0.4f, speed01);

        // Shoved along the round, in the body's own space. Only the torso moves: the
        // feet are planted, so anything bigger than a nudge pulls the body off its legs.
        Vector3 shove = new Vector3(_hitLocal.x, 0f, _hitLocal.z) * (_hit01 * hitShove);

        // A leg shot drops the body a little onto the knee that buckled.
        bool legHit = _hitPart == BodyPart.LeftLeg || _hitPart == BodyPart.RightLeg;
        float buckle = legHit ? _hit01 * 0.09f : 0f;

        float lowered = (_torsoRestPosition.y - crawlHeight) * _crawl01;

        torso.localPosition = _torsoRestPosition + Vector3.up * (bob - dip - buckle - lowered) + shove;

        float lean = runLean * speed01;

        // The attack leans further in and death folds the whole thing forward, so the
        // poses share one axis instead of fighting over the transform.
        lean = Mathf.Lerp(lean, runLean + 12f, _attack01);
        lean = Mathf.Lerp(lean, crawlPitch, _crawl01);
        lean = Mathf.Lerp(lean, deathSlump, _death01);

        // The hit is added last and on both axes, because it is the one pose that is not
        // a state the enemy is in -- it happens on top of whatever it was already doing.
        // Pitch folds it over or throws it back along the bullet; roll is what stops a
        // shot from the side reading identically to one from the front.
        float throwScale = _hitPart == BodyPart.Torso ? 1f : 0.45f;
        float pitch = lean + _hitLocal.z * _hit01 * hitThrow * throwScale;
        float roll = -_hitLocal.x * _hit01 * hitThrow * 0.8f * throwScale;

        roll += limp * stance * 7f * -side;
        if (legHit) roll += (_hitPart == BodyPart.LeftLeg ? 1f : -1f) * _hit01 * 12f;

        // Crawling, it rocks side to side with each pull.
        roll += Mathf.Sin(_stridePhase) * 8f * _crawl01 * speed01;

        torso.localRotation = _torsoRest * Quaternion.Euler(pitch, 0f, roll);
        return pitch;
    }

    /// <summary>0 sound, 1 fully limping. Off while crawling, which is its own pose.</summary>
    float LimpWeight => _wounds != null && _wounds.Legs == EnemyWounds.LegState.Limping ? 1f - _crawl01 : 0f;

    /// <summary>-1 for the left leg, +1 for the right.</summary>
    float BadLegSide => _wounds != null && _wounds.WorseLeg == BodyPart.LeftLeg ? -1f : 1f;

    // ======================================================================
    // Legs
    // ======================================================================

    void ApplyLegs(float speed01)
    {
        float swing = Mathf.Sin(_stridePhase) * strideAngle * speed01;

        // The knee folds as the leg comes forward (the swing is rising) and is nearly
        // straight as it lands and takes the weight.
        float leftKnee = KneeFold(_stridePhase, speed01);
        float rightKnee = KneeFold(_stridePhase + Mathf.PI, speed01);

        float limp = LimpWeight;
        float side = BadLegSide;

        // The bad leg takes a short, stiff step.
        float leftSwing = swing * (side < 0f ? Mathf.Lerp(1f, 0.5f, limp) : 1f);
        float rightSwing = -swing * (side > 0f ? Mathf.Lerp(1f, 0.5f, limp) : 1f);
        if (side < 0f) leftKnee *= Mathf.Lerp(1f, 0.3f, limp);
        else rightKnee *= Mathf.Lerp(1f, 0.3f, limp);

        // A leg shot buckles that knee for a moment.
        if (_hitPart == BodyPart.LeftLeg) leftKnee += _hit01 * 50f;
        if (_hitPart == BodyPart.RightLeg) rightKnee += _hit01 * 50f;

        // Crawling: the legs trail out behind, lowered with the hips, kicking a little.
        float trail = 88f * _crawl01;
        float kick = Mathf.Sin(_stridePhase) * 14f * _crawl01 * speed01;
        float lowered = (_torsoRestPosition.y - crawlHeight) * _crawl01;

        PoseLeg(leftLeg, _leftLegRest, _leftLegRestPosition, leftSwing * (1f - _crawl01) + trail, lowered);
        PoseLeg(rightLeg, _rightLegRest, _rightLegRestPosition, rightSwing * (1f - _crawl01) + trail, lowered);

        SetJoint(leftShin, _leftShinRest, leftKnee * (1f - _crawl01) + (20f + kick) * _crawl01);
        SetJoint(rightShin, _rightShinRest, rightKnee * (1f - _crawl01) + (20f - kick) * _crawl01);
    }

    float KneeFold(float phase, float speed01)
    {
        // A little bend standing still, so the legs do not look locked.
        return 6f + kneeBend * speed01 * Mathf.Max(0f, Mathf.Cos(phase));
    }

    void PoseLeg(Transform leg, Quaternion rest, Vector3 restPosition, float pitch, float lowered)
    {
        if (leg == null) return;

        leg.localRotation = rest * Quaternion.Euler(pitch, 0f, 0f);
        leg.localPosition = restPosition + Vector3.down * lowered;
    }

    // ======================================================================
    // Arms
    // ======================================================================

    void ApplyArms(float speed01, float torsoPitch)
    {
        float swing = Mathf.Sin(_stridePhase) * strideAngle * speed01;
        float armSwing = -swing * armSwingFactor;

        bool armed = Armed;

        // Held level against whatever the body is doing: the lean, the crawl, the hit.
        // Without that the rifle points at the floor every time the enemy runs.
        float recoil = 0f;
        if (_ai != null)
        {
            float since = Time.time - _ai.LastShotTime;
            if (since >= 0f && since < 0.14f) recoil = recoilKick * (1f - since / 0.14f);
        }

        _recoil = recoil;

        float aimHold = -aimRaise - torsoPitch - _aimPitch - recoil;

        float hold = armed ? aimHold : 0f;

        // The attack pose pulls the arms the rest of the way up, so the two blend instead
        // of fighting. A crawler cannot swing; it claws.
        hold = Mathf.Lerp(hold, -attackRaise, _attack01 * (1f - _crawl01));

        // Swing fades out as the attack takes over, so a lunging enemy is not also
        // pumping its arms, and a carried rifle barely moves at all.
        float weight = (1f - _attack01) * (armed ? armedSwingFactor : 1f);

        float leftHold = hold, rightHold = hold;
        float leftSwing = armSwing * weight, rightSwing = -armSwing * weight;

        // Crawling without a rifle: arms reach ahead and pull, one after the other.
        if (!armed && _crawl01 > 0f)
        {
            float reach = -(90f + torsoPitch);
            float pull = Mathf.Sin(_stridePhase) * 35f * Mathf.Max(0.3f, speed01);
            leftHold = Mathf.Lerp(leftHold, reach + pull, _crawl01);
            rightHold = Mathf.Lerp(rightHold, reach - pull, _crawl01);
            leftSwing *= 1f - _crawl01;
            rightSwing *= 1f - _crawl01;
        }

        // A hit opens the guard: the arm that was hit is thrown back hard, the other
        // less so. Added to the hold, so a rifle is knocked off aim rather than reset.
        float thrown = _hit01 * hitThrow * hitArmThrow;
        leftHold += thrown * (_hitPart == BodyPart.LeftArm ? 2f : _hitPart == BodyPart.RightArm ? 0.3f : 1f);
        rightHold += thrown * (_hitPart == BodyPart.RightArm ? 2f : _hitPart == BodyPart.LeftArm ? 0.3f : 1f);

        // A wrecked arm hangs. It still sways with the body, but it is not holding anything.
        float leftHang = ArmHang(BodyPart.LeftArm);
        float rightHang = ArmHang(BodyPart.RightArm);
        if (armed) leftHang = rightHang = 0f;

        leftHold = Mathf.Lerp(leftHold, -torsoPitch * 0.8f, leftHang);
        rightHold = Mathf.Lerp(rightHold, -torsoPitch * 0.8f, rightHang);

        SetLimb(leftArm, _leftArmRest, leftHold + leftSwing, leftHang * 8f);
        SetLimb(rightArm, _rightArmRest, rightHold + rightSwing, -rightHang * 8f);

        // Elbows: straight on the rifle, bent and pumping when running empty-handed,
        // bent to pull when crawling, loose when the arm is wrecked.
        float runBend = -(20f + 45f * speed01) * (1f - _attack01);
        float leftElbow = armed ? -8f : runBend;
        float rightElbow = armed ? -8f : runBend;

        if (!armed && _crawl01 > 0f)
        {
            leftElbow = Mathf.Lerp(leftElbow, -30f - Mathf.Max(0f, Mathf.Sin(_stridePhase)) * 60f, _crawl01);
            rightElbow = Mathf.Lerp(rightElbow, -30f - Mathf.Max(0f, -Mathf.Sin(_stridePhase)) * 60f, _crawl01);
        }

        leftElbow = Mathf.Lerp(leftElbow, -5f, leftHang);
        rightElbow = Mathf.Lerp(rightElbow, -5f, rightHang);

        SetJoint(leftForearm, _leftForeRest, leftElbow);
        SetJoint(rightForearm, _rightForeRest, rightElbow);
    }

    /// <summary>
    /// Aims the rifle: level whatever the body is leaning, tipped at the player, kicking
    /// with each round and knocked off line by a hit. Thrust out butt-first when a
    /// shooter is swinging at somebody in its face, and held out along the floor when
    /// it is crawling.
    /// </summary>
    void PoseWeapon(float torsoPitch)
    {
        var t = weapon.transform;

        float strike = _attack01 * (1f - _crawl01);
        bool swinging = _ai != null && _ai.target != null &&
                        Vector3.Distance(transform.position, _ai.target.position) <= _ai.meleeRange + 0.5f;
        if (!swinging) strike *= 0.15f;

        float pitch = -torsoPitch - _aimPitch - _recoil + _hit01 * hitThrow * 0.7f - 35f * strike;

        // Upright, the rifle rides the chest wherever it leans. Prone, it is placed as if
        // the body were level, out in front of the face, because the chest-high spot of a
        // standing body is under the floor once the body is lying down.
        Quaternion unlean = Quaternion.Inverse(Quaternion.Euler(torsoPitch, 0f, 0f));
        Vector3 standing = _weaponRestPosition;
        Vector3 prone = unlean * proneWeaponPosition;

        Vector3 position = Vector3.Lerp(standing, prone, _crawl01);
        position += Vector3.back * (_recoil * 0.006f);
        position += unlean * (Vector3.forward * 0.25f + Vector3.up * 0.1f) * strike;

        t.localPosition = position;
        t.localRotation = _weaponRestRotation * Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>
    /// Two-bone reach: turns the upper arm and bends the elbow so the hand lands on a
    /// point, with the elbow falling down and out to the side the way an elbow does.
    /// Past full reach the arm straightens toward it rather than stretching.
    ///
    /// The bones hang along their pivot's -Y and an elbow bends about its own X, which
    /// is the convention the rest of this class poses by, so the result is a pose the
    /// walk, the ragdoll and the death poses all understand.
    /// </summary>
    void ReachFor(Transform shoulder, Transform elbow, Quaternion elbowRest, Vector3 target, float side)
    {
        if (shoulder == null || elbow == null || torso == null) return;

        float scale = Mathf.Max(0.01f, shoulder.lossyScale.y);
        float upper = elbow.localPosition.magnitude * scale;
        float lower = handReach * scale;

        Vector3 origin = shoulder.position;
        Vector3 toTarget = target - origin;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
        Vector3 direction = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : -torso.up;

        // Down, out to its own side, and a little back: where an elbow hangs.
        Vector3 pole = -torso.up + torso.right * (0.7f * side) - torso.forward * 0.2f;
        Vector3 across = Vector3.ProjectOnPlane(pole, direction);
        if (across.sqrMagnitude < 0.000001f) across = Vector3.ProjectOnPlane(-torso.up, direction);
        across.Normalize();

        // Law of cosines for the angle at the shoulder.
        float cosShoulder = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f);
        float sinShoulder = Mathf.Sqrt(1f - cosShoulder * cosShoulder);

        Vector3 elbowPoint = origin + direction * (upper * cosShoulder) + across * (upper * sinShoulder);
        Vector3 hand = origin + direction * distance;

        Vector3 upperDir = (elbowPoint - origin).normalized;
        Vector3 lowerDir = (hand - elbowPoint).normalized;

        // The upper arm's -Y down the bone, its +Z toward where the forearm goes, so
        // bending the elbow about X swings the forearm to the hand.
        Vector3 facing = Vector3.ProjectOnPlane(lowerDir, upperDir);
        if (facing.sqrMagnitude < 0.000001f) facing = Vector3.ProjectOnPlane(torso.forward, upperDir);

        shoulder.rotation = Quaternion.LookRotation(facing.normalized, -upperDir);
        elbow.localRotation = elbowRest * Quaternion.Euler(-Vector3.Angle(upperDir, lowerDir), 0f, 0f);
    }

    /// <summary>0 to 1: how limp an arm hangs. The gun arm once disarmed, either arm once wrecked.</summary>
    float ArmHang(BodyPart arm)
    {
        if (_wounds == null) return 0f;

        float severity = _wounds.Severity(arm);
        if (arm == BodyPart.RightArm && _wounds.Disarmed) severity = 1f;

        return Mathf.Clamp01((severity - 0.6f) / 0.4f) * (1f - _attack01 * 0.5f);
    }

    // ======================================================================
    // Head
    // ======================================================================

    /// <summary>Turns the head to the player, lifts it off the floor when crawling, and snaps it back on a headshot.</summary>
    void ApplyHead(float torsoPitch)
    {
        if (head == null) return;

        float yaw = 0f;

        if (_ai != null && _ai.target != null && _ai.HasSpotted)
        {
            Vector3 local = transform.InverseTransformPoint(_ai.target.position);
            yaw = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(0.01f, local.z)) * Mathf.Rad2Deg, -55f, 55f);
        }

        // Looking at the player, not at the floor the body is leaning over.
        float pitch = -torsoPitch * 0.85f - _aimPitch * 0.7f;

        if (_hitPart == BodyPart.Head)
            pitch += _hitLocal.z * _hit01 * hitThrow * 1.6f;

        head.localRotation = _headRest * Quaternion.Euler(pitch, yaw, 0f);
    }

    // ======================================================================

    /// <summary>Pitches a limb about its pivot, with a little roll to splay it.</summary>
    static void SetLimb(Transform limb, Quaternion rest, float pitch, float roll)
    {
        if (limb == null) return;
        limb.localRotation = rest * Quaternion.Euler(pitch, 0f, roll);
    }

    /// <summary>Bends an elbow or knee about its pivot.</summary>
    static void SetJoint(Transform joint, Quaternion rest, float bend)
    {
        if (joint == null) return;
        joint.localRotation = rest * Quaternion.Euler(bend, 0f, 0f);
    }

    /// <summary>
    /// Lets go of the rifle: a copy of it falls to the floor as a real object, pushed
    /// along <paramref name="push"/>, and the one in the hands is hidden for good. Called
    /// when the gun arm is wrecked and when the enemy dies. Does nothing if it is not
    /// holding one.
    /// </summary>
    public void DropWeapon(Vector3 push)
    {
        if (_dropped || weapon == null) return;

        bool wasHeld = weapon.activeInHierarchy;
        _dropped = true;
        weapon.SetActive(false);

        if (!wasHeld) return;

        var t = weapon.transform;
        var dropped = Instantiate(weapon, t.position, t.rotation);
        dropped.name = "DroppedRifle";
        dropped.transform.localScale = t.lossyScale;
        // The muzzle flash lives on the barrel; a copy of it would fire once on the floor.
        foreach (var flash in dropped.GetComponentsInChildren<TransientFlash>(true))
            Destroy(flash.gameObject);

        dropped.SetActive(true);

        foreach (var filter in dropped.GetComponentsInChildren<MeshFilter>())
        {
            filter.gameObject.layer = 2;   // Ignore Raycast: not cover, not a target
            filter.gameObject.AddComponent<BoxCollider>();
        }

        dropped.layer = 2;

        var body = dropped.AddComponent<Rigidbody>();
        body.mass = 3.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Vector3 v = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;
        if (push.sqrMagnitude > 0.0001f) v += push.normalized * 1.8f;
        body.linearVelocity = v + Vector3.up * 0.8f;
        body.angularVelocity = Random.insideUnitSphere * 4f;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var mine = dropped.GetComponentsInChildren<Collider>();
            foreach (var theirs in player.GetComponentsInChildren<Collider>())
                foreach (var c in mine)
                    if (theirs != null && c != null) Physics.IgnoreCollision(theirs, c);
        }

        Destroy(dropped, 15f);
    }
}
