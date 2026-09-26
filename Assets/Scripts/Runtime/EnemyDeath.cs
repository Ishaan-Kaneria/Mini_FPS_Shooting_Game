using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// How an enemy dies, decided by what killed it, played out by physics.
///
/// The built enemy has no rig, so this turns its limb pivots into a ragdoll the moment it
/// dies: a rigidbody on each segment and a joint to the one it hangs from, with limits a
/// body actually has -- a knee bends one way, an elbow the other, a neck does not turn
/// round. Then the killing hit decides how it goes down:
///
/// - **Headshot**: it drops where it stands. No stagger, no scream, knees first.
/// - **Blast**: thrown, away from the blast and upward, turning as it goes.
/// - **Heavy hit** (a shotgun at close range, a burst that lands together): blown back
///   along the shot, off its feet.
/// - **Leg**: the leg goes out first -- it buckles toward that side and falls over it.
/// - **Gut or chest, lighter**: it doubles over or staggers back a step, then falls.
/// - **Punch**: spun round by the blow.
///
/// Whatever it was doing carries into the fall: a running enemy shot dead goes down
/// forward at its own speed, which is most of what makes a death look like physics and
/// not like an animation playing.
///
/// Corpses stay a while, then settle and sink. Only so many may be simulating at once;
/// past that the oldest is frozen where it lies, so a grenade into a crowd costs a moment,
/// not the rest of the level.
///
/// A rigged character with its own <see cref="RagdollController"/> uses
/// <see cref="Blow"/> for the same push, and does not need this component.
/// </summary>
[DisallowMultipleComponent]
public class EnemyDeath : MonoBehaviour
{
    public enum Style { Drop, Thrown, BlownBack, Buckle, DoubleOver, StaggerBack, Spun }

    [Header("Body")]
    [Tooltip("The hips pivot the upper body hangs from. The ragdoll's root.")]
    public Transform torso;
    public Transform head;
    public Transform leftUpperArm, leftForearm, rightUpperArm, rightForearm;
    public Transform leftThigh, leftShin, rightThigh, rightShin;

    [Header("Falling")]
    [Tooltip("Total mass of the body, in kilograms. Split across the segments the way a " +
             "person's is, so a push on an arm moves an arm and not the whole body.")]
    [Min(10f)] public float bodyMass = 75f;

    [Tooltip("Share of the body's damage that counts as a heavy hit, which blows it off its feet.")]
    [Range(0.1f, 2f)] public float heavyShare = 0.55f;

    [Tooltip("Push of an ordinary killing round, in newton-seconds. Scaled up by how hard it hit.")]
    public Vector2 roundImpulse = new Vector2(16f, 75f);

    [Tooltip("Push of a blast at full strength.")]
    public float blastImpulse = 320f;

    [Tooltip("Push of a punch.")]
    public float punchImpulse = 90f;

    [Header("Corpse")]
    [Tooltip("Seconds the body is simulated before it is frozen where it lies.")]
    [Min(0.5f)] public float settleTime = 4.5f;

    [Tooltip("Seconds it takes to sink out of sight at the end. Taken from the end of " +
             "Health.destroyDelay, so it is gone underground before it is destroyed.")]
    [Min(0f)] public float sinkTime = 1.8f;

    [Tooltip("Ragdolls simulating at once, per quality tier (Low, Medium, High).")]
    public Vector3Int activeBudget = new Vector3Int(4, 7, 10);

    public bool IsRagdolled { get; private set; }

    /// <summary>How the last death went. For a test, and for anything that wants to know.</summary>
    public Style LastStyle { get; private set; }

    Health _health;
    EnemyWounds _wounds;
    NavMeshAgent _agent;
    List<Rigidbody> _bodies = new List<Rigidbody>();
    float _diedAt;

    /// <summary>
    /// Each segment's pose as built, which is where the joint limits are measured from.
    /// Captured in Awake, before anything has animated.
    /// </summary>
    Quaternion[] _rest;

    static readonly List<EnemyDeath> Active = new List<EnemyDeath>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Active.Clear();

    void Awake()
    {
        _health = GetComponent<Health>();
        _wounds = GetComponent<EnemyWounds>();
        _agent = GetComponent<NavMeshAgent>();

        var segments = Segments;
        _rest = new Quaternion[segments.Length];
        for (int i = 0; i < segments.Length; i++)
            _rest[i] = segments[i] != null ? segments[i].localRotation : Quaternion.identity;
    }

    Transform[] Segments => new[]
    {
        torso, head, leftUpperArm, leftForearm, rightUpperArm, rightForearm,
        leftThigh, leftShin, rightThigh, rightShin
    };

    void OnEnable()
    {
        if (_health != null) _health.Died += OnDied;
    }

    void OnDisable()
    {
        if (_health != null) _health.Died -= OnDied;
    }

    void OnDestroy() => Active.Remove(this);

    void OnDied(Health health)
    {
        if (IsRagdolled || !isActiveAndEnabled) return;

        var info = health.LastDamage;
        Vector3 carried = _agent != null && _agent.enabled ? _agent.velocity : Vector3.zero;

        _diedAt = Time.time;
        LastStyle = Choose(info);
        StartCoroutine(Die(info, LastStyle, carried));
    }

    Style Choose(DamageInfo info)
    {
        float burst = _wounds != null ? _wounds.RecentDamage : info.amount;
        bool crawling = _wounds != null && _wounds.Legs == EnemyWounds.LegState.Crawling;

        return Classify(info, burst / Mathf.Max(1f, _health.maxHealth), crawling, heavyShare);
    }

    /// <summary>
    /// Reads the killing blow. The order is the priority: a headshot is a headshot however
    /// hard it hit. Static so a rigged enemy's <see cref="RagdollController"/> falls by the
    /// same rules.
    /// </summary>
    public static Style Classify(DamageInfo info, float burstShare, bool crawling, float heavyShare = 0.55f)
    {
        if (info.isHeadshot || info.part == BodyPart.Head) return Style.Drop;
        if (info.fromBlast) return Style.Thrown;
        if (info.fromMelee) return Style.Spun;
        if (burstShare >= heavyShare) return Style.BlownBack;

        // On the floor already: there is nowhere further to fall from.
        if (crawling) return Style.Drop;

        if (info.part == BodyPart.LeftLeg || info.part == BodyPart.RightLeg) return Style.Buckle;

        // An even chance of folding over the wound or being knocked back a step by it.
        return Random.value < 0.5f ? Style.DoubleOver : Style.StaggerBack;
    }

    IEnumerator Die(DamageInfo info, Style style, Vector3 carried)
    {
        // Let go of the rifle. It falls with the body rather than floating in the air
        // where the hands were, or vanishing with the corpse.
        var limbs = GetComponent<EnemyLimbAnimator>();
        if (limbs != null) limbs.DropWeapon(info.direction);

        float before = PreFallSeconds(style);
        if (before > 0f) yield return PreFall(style, info, before);

        GoLimp(carried);
        Push(info, style);

        StartCoroutine(Corpse());
    }

    static float PreFallSeconds(Style style)
    {
        switch (style)
        {
            case Style.Buckle: return Random.Range(0.22f, 0.32f);
            case Style.DoubleOver: return Random.Range(0.3f, 0.45f);
            case Style.StaggerBack: return Random.Range(0.25f, 0.4f);
            default: return 0f;
        }
    }

    // ======================================================================
    // The moment before it falls
    // ======================================================================

    /// <summary>
    /// The half-second a body still stands for after the shot that killed it: a knee
    /// going, a fold over the wound, a step back. Posed by hand from wherever the limbs
    /// were, then handed to physics in exactly that pose, so the fall carries on from it.
    /// </summary>
    IEnumerator PreFall(Style style, DamageInfo info, float seconds)
    {
        var torsoFrom = torso != null ? torso.localRotation : Quaternion.identity;
        var torsoPosFrom = torso != null ? torso.localPosition : Vector3.zero;

        var pose = new List<(Transform t, Quaternion from, Quaternion to)>();

        Vector3 local = transform.InverseTransformDirection(info.direction.sqrMagnitude > 0.0001f
            ? info.direction.normalized
            : -transform.forward);

        float side = info.part == BodyPart.LeftLeg ? -1f : 1f;
        Quaternion torsoTo = torsoFrom;
        Vector3 drop = Vector3.zero;
        Vector3 step = Vector3.zero;

        switch (style)
        {
            case Style.Buckle:
            {
                // The shot leg folds, the other takes the weight for a moment, and the
                // body leans over the one that went.
                var thigh = side < 0f ? leftThigh : rightThigh;
                var shin = side < 0f ? leftShin : rightShin;
                Add(pose, thigh, Quaternion.Euler(-35f, 0f, 0f));
                Add(pose, shin, Quaternion.Euler(95f, 0f, 0f));
                Add(pose, side < 0f ? rightShin : leftShin, Quaternion.Euler(30f, 0f, 0f));
                torsoTo = torsoFrom * Quaternion.Euler(22f, 0f, 18f * -side);
                drop = Vector3.down * 0.35f;
                break;
            }

            case Style.DoubleOver:
                // Folds over the wound: both hands to it, knees going.
                Add(pose, leftUpperArm, Quaternion.Euler(-55f, 0f, -25f));
                Add(pose, rightUpperArm, Quaternion.Euler(-55f, 0f, 25f));
                Add(pose, leftForearm, Quaternion.Euler(-70f, 0f, 0f));
                Add(pose, rightForearm, Quaternion.Euler(-70f, 0f, 0f));
                Add(pose, leftShin, Quaternion.Euler(45f, 0f, 0f));
                Add(pose, rightShin, Quaternion.Euler(45f, 0f, 0f));
                Add(pose, leftThigh, Quaternion.Euler(-30f, 0f, 0f));
                Add(pose, rightThigh, Quaternion.Euler(-30f, 0f, 0f));
                torsoTo = torsoFrom * Quaternion.Euler(38f, 0f, 0f);
                drop = Vector3.down * 0.25f;
                break;

            case Style.StaggerBack:
                // Knocked back a step along the round, arms thrown up.
                Add(pose, leftUpperArm, Quaternion.Euler(-100f, 0f, -20f));
                Add(pose, rightUpperArm, Quaternion.Euler(-80f, 0f, 20f));
                Add(pose, leftThigh, Quaternion.Euler(-25f, 0f, 0f));
                torsoTo = torsoFrom * Quaternion.Euler(-18f * Mathf.Sign(local.z + 0.001f), 0f, -local.x * 15f);
                step = new Vector3(local.x, 0f, local.z).normalized * 0.35f;
                break;
        }

        // The pose is added to wherever each limb already was.
        for (int i = 0; i < pose.Count; i++)
            pose[i] = (pose[i].t, pose[i].t.localRotation, pose[i].t.localRotation * pose[i].to);

        Vector3 rootFrom = transform.position;
        Vector3 rootTo = rootFrom + transform.TransformDirection(step);

        float start = Time.time;

        while (Time.time - start < seconds)
        {
            float t = Mathf.Clamp01((Time.time - start) / seconds);

            // Fast out of the hit, slowing into the fall.
            float e = 1f - (1f - t) * (1f - t);

            foreach (var (limb, from, to) in pose)
                if (limb != null) limb.localRotation = Quaternion.Slerp(from, to, e);

            if (torso != null)
            {
                torso.localRotation = Quaternion.Slerp(torsoFrom, torsoTo, e);
                torso.localPosition = torsoPosFrom + drop * e;
            }

            transform.position = Vector3.Lerp(rootFrom, rootTo, e);
            yield return null;
        }
    }

    static void Add(List<(Transform, Quaternion, Quaternion)> pose, Transform limb, Quaternion delta)
    {
        if (limb != null) pose.Add((limb, Quaternion.identity, delta));
    }

    // ======================================================================
    // The ragdoll
    // ======================================================================

    /// <summary>
    /// A rigidbody on every segment and a joint to the one it hangs from. Built at the
    /// moment of death rather than kept kinematic all game, because forty enemies each
    /// carrying ten sleeping bodies is forty times the physics scene for nothing.
    /// </summary>
    void GoLimp(Vector3 carried)
    {
        if (IsRagdolled) return;
        IsRagdolled = true;

        Active.Add(this);
        EnforceBudget();

        if (_agent != null && _agent.enabled) _agent.enabled = false;

        // Joint limits are measured from the pose the joint is made in. Made in whatever
        // pose the body died in, a knee bent by the fall would get another 140 degrees on
        // top of the bend -- so every segment goes back to its built pose for the moment
        // the joints are made, and straight back to where it was before anything draws.
        var segments = Segments;
        var died = new Quaternion[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            died[i] = segments[i].localRotation;
            if (_rest != null && i < _rest.Length) segments[i].localRotation = _rest[i];
        }

        var torsoBody = Segment(torso, 0.43f);
        var headBody = Segment(head, 0.08f);

        var leftUpper = Segment(leftUpperArm, 0.03f);
        var leftFore = Segment(leftForearm, 0.02f);
        var rightUpper = Segment(rightUpperArm, 0.03f);
        var rightFore = Segment(rightForearm, 0.02f);

        var leftThighBody = Segment(leftThigh, 0.1f);
        var leftShinBody = Segment(leftShin, 0.06f);
        var rightThighBody = Segment(rightThigh, 0.1f);
        var rightShinBody = Segment(rightShin, 0.06f);

        // Neck: nods and tips, barely turns.
        Joint(headBody, torsoBody, -40f, 30f, 25f, 20f);

        // Shoulders: a wide cone. Elbows: forward only.
        // Shoulders: a wide cone, and far forward -- a rifle is carried with the arms
        // raised seventy-odd degrees, and a limit tighter than that snaps them down.
        Joint(leftUpper, torsoBody, -120f, 60f, 80f, 60f);
        Joint(rightUpper, torsoBody, -120f, 60f, 80f, 60f);
        Joint(leftFore, leftUpper != null ? leftUpper : torsoBody, -135f, 0f, 10f, 10f);
        Joint(rightFore, rightUpper != null ? rightUpper : torsoBody, -135f, 0f, 10f, 10f);

        // Hips: forward a long way, back a little. Knees: backward only.
        Joint(leftThighBody, torsoBody, -100f, 25f, 35f, 20f);
        Joint(rightThighBody, torsoBody, -100f, 25f, 35f, 20f);
        Joint(leftShinBody, leftThighBody != null ? leftThighBody : torsoBody, 0f, 140f, 5f, 5f);
        Joint(rightShinBody, rightThighBody != null ? rightThighBody : torsoBody, 0f, 140f, 5f, 5f);

        for (int i = 0; i < segments.Length; i++)
            if (segments[i] != null) segments[i].localRotation = died[i];

        IgnoreSelfAndPlayer();

        foreach (var body in _bodies)
        {
            body.linearVelocity = carried;
            body.WakeUp();
        }
    }

    Rigidbody Segment(Transform segment, float massShare)
    {
        if (segment == null) return null;

        var body = segment.GetComponent<Rigidbody>();
        if (body == null) body = segment.gameObject.AddComponent<Rigidbody>();

        body.mass = Mathf.Max(0.5f, bodyMass * massShare);
        body.linearDamping = 0.05f;
        body.angularDamping = 0.6f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.solverIterations = 12;
        body.isKinematic = false;

        _bodies.Add(body);
        return body;
    }

    /// <summary>
    /// A joint whose twist is the pitch of the limb -- the axis a knee and an elbow bend
    /// about -- so a one-way hinge is simply a twist range with zero at one end.
    /// </summary>
    static void Joint(Rigidbody body, Rigidbody parent, float lowTwist, float highTwist,
                      float swing1, float swing2)
    {
        if (body == null || parent == null) return;

        var joint = body.gameObject.AddComponent<CharacterJoint>();
        joint.connectedBody = parent;
        joint.axis = Vector3.right;
        joint.swingAxis = Vector3.forward;
        joint.enablePreprocessing = false;
        joint.enableProjection = true;

        joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
        joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
        joint.swing1Limit = new SoftJointLimit { limit = swing1 };
        joint.swing2Limit = new SoftJointLimit { limit = swing2 };

        // A little give at the limit, rather than a wall to bounce off.
        var spring = new SoftJointLimitSpring { spring = 0f, damper = 4f };
        joint.twistLimitSpring = spring;
        joint.swingLimitSpring = spring;
    }

    /// <summary>
    /// Parts of one body never collide with each other -- the built enemy's limbs overlap
    /// its chest at the shoulder, and a ragdoll fighting itself is a ragdoll that jitters
    /// forever. Nor with the player, who should never trip on a body they are walking
    /// over or be shoved by one falling on them.
    /// </summary>
    void IgnoreSelfAndPlayer()
    {
        var own = GetComponentsInChildren<Collider>();

        for (int i = 0; i < own.Length; i++)
            for (int j = i + 1; j < own.Length; j++)
                if (own[i] != null && own[j] != null) Physics.IgnoreCollision(own[i], own[j]);

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        foreach (var theirs in player.GetComponentsInChildren<Collider>())
            foreach (var mine in own)
                if (theirs != null && mine != null) Physics.IgnoreCollision(theirs, mine);
    }

    // ======================================================================
    // The push
    // ======================================================================

    void Push(DamageInfo info, Style style)
    {
        if (_bodies.Count == 0) return;

        float share = _health != null && _health.maxHealth > 0f
            ? (_wounds != null ? Mathf.Max(_wounds.RecentDamage, info.amount) : info.amount) / _health.maxHealth
            : 0.5f;

        Vector3 impulse = Blow(info, style, share, this);
        var at = Nearest(info.point);

        switch (style)
        {
            case Style.Drop:
                // A small nod along the round and nothing else: the legs simply stop
                // holding it up.
                if (at != null) at.AddForceAtPosition(impulse, info.point, ForceMode.Impulse);
                break;

            case Style.Thrown:
                // The whole body goes, and spins -- a blast is not a push on one point.
                foreach (var body in _bodies)
                {
                    body.AddForce(impulse * (body.mass / bodyMass), ForceMode.Impulse);
                    body.AddTorque(Random.insideUnitSphere * body.mass * 2.5f, ForceMode.Impulse);
                }
                break;

            case Style.Spun:
                if (at != null) at.AddForceAtPosition(impulse, info.point, ForceMode.Impulse);
                if (torso != null && torso.TryGetComponent(out Rigidbody spin))
                    spin.AddTorque(Vector3.up * Random.Range(-1f, 1f) * spin.mass * 6f, ForceMode.Impulse);
                break;

            case Style.BlownBack:
                // Off its feet: most of the push into the torso, some lift, and the feet
                // left behind.
                if (torso != null && torso.TryGetComponent(out Rigidbody chest))
                    chest.AddForceAtPosition(impulse * 0.7f + Vector3.up * impulse.magnitude * 0.15f,
                                             info.point, ForceMode.Impulse);
                if (at != null) at.AddForceAtPosition(impulse * 0.3f, info.point, ForceMode.Impulse);
                break;

            default:
                if (at != null) at.AddForceAtPosition(impulse, info.point, ForceMode.Impulse);
                break;
        }
    }

    /// <summary>
    /// The push a death gives the body, as a world-space impulse. Shared with
    /// <see cref="RagdollController"/>, so a rigged enemy falls by the same rules.
    /// </summary>
    public static Vector3 Blow(DamageInfo info, Style style, float share, EnemyDeath tuning = null)
    {
        Vector3 direction = info.direction.sqrMagnitude > 0.0001f ? info.direction.normalized : Vector3.forward;

        Vector2 round = tuning != null ? tuning.roundImpulse : new Vector2(16f, 75f);
        float blast = tuning != null ? tuning.blastImpulse : 320f;
        float punch = tuning != null ? tuning.punchImpulse : 90f;

        switch (style)
        {
            case Style.Drop:
                return direction * round.x * 0.5f;

            case Style.Thrown:
                return (direction + Vector3.up * 0.8f).normalized * blast * Mathf.Clamp(share, 0.35f, 1.2f);

            case Style.Spun:
                return (direction + Vector3.up * 0.2f).normalized * punch;

            case Style.BlownBack:
                return direction * Mathf.Lerp(round.y, round.y * 2.2f, Mathf.Clamp01(share - 0.5f));

            default:
                return direction * Mathf.Lerp(round.x, round.y, Mathf.Clamp01(share));
        }
    }

    Rigidbody Nearest(Vector3 point)
    {
        Rigidbody best = null;
        float bestDistance = float.MaxValue;

        foreach (var body in _bodies)
        {
            if (body == null) continue;

            float distance = (body.worldCenterOfMass - point).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = body;
        }

        return best;
    }

    // ======================================================================
    // The corpse
    // ======================================================================

    IEnumerator Corpse()
    {
        yield return new WaitForSeconds(settleTime);
        Freeze();

        // Sink out of sight before Health destroys it, so a body never pops out of existence
        // in front of the player.
        float lifetime = _health != null && _health.destroyOnDeath ? _health.destroyDelay : 0f;
        if (lifetime <= 0f || sinkTime <= 0f) yield break;

        float wait = lifetime - sinkTime - (Time.time - _diedAt);

        if (wait > 0f) yield return new WaitForSeconds(wait);

        Vector3 from = transform.position;
        Vector3 to = from + Vector3.down * 0.8f * Mathf.Max(0.5f, transform.lossyScale.y);
        float start = Time.time;

        while (Time.time - start < sinkTime)
        {
            transform.position = Vector3.Lerp(from, to, (Time.time - start) / sinkTime);
            yield return null;
        }
    }

    /// <summary>Stops simulating: every body kinematic where it lies.</summary>
    void Freeze()
    {
        Active.Remove(this);

        foreach (var body in _bodies)
        {
            if (body == null) continue;

            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
        }
    }

    /// <summary>Freezes the oldest bodies still falling once there are more than the tier allows.</summary>
    static void EnforceBudget()
    {
        Active.RemoveAll(d => d == null);
        if (Active.Count == 0) return;

        int budget = Mathf.Max(1, PickBudget(Active[Active.Count - 1].activeBudget));

        while (Active.Count > budget)
        {
            var oldest = Active[0];
            Active.RemoveAt(0);
            if (oldest != null) oldest.Freeze();
        }
    }

    static int PickBudget(Vector3Int budget)
    {
        switch (QualityTiers.Current)
        {
            case GameSettings.Quality.Low: return budget.x;
            case GameSettings.Quality.Medium: return budget.y;
            default: return budget.z;
        }
    }
}
