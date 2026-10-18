using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The enemy's rifle as a real object of about three and a half kilos.
///
/// - **Held.** It rides in the right hand (a socket under the hand bone, set by
///   FPSKitRiggedSoldier from the aiming clip) and the left hand is reached onto the handguard.
///   Between the socket and the rifle sits a spring: when the body starts, stops or turns,
///   the rifle lags a few centimetres and degrees and settles, so it has weight. Recoil, the
///   sag of a body in pain and the butt stroke are laid on the same spring.
/// - **A fall** (<see cref="OnFall"/>). A light one leaves it on the sling: a physics body on
///   a short strap from the chest, with tight swing limits and heavy damping, so it thumps
///   against the body and hangs. A hard one knocks it out of the hands: a free body that
///   lands, rolls a little and lies, with friction enough not to slide. Between the profile's
///   two speeds the chance of dropping it rises with the speed of the fall.
/// - **Getting it back** (<see cref="BeginRecover"/>). Off the sling: the right hand reaches
///   down for it and brings it up to the ready, and the left hand takes the handguard. On the
///   ground and close enough, with the player far enough: it walks over, crouches, picks it
///   up. Otherwise it goes without (<see cref="Lost"/>; EnemyBody turns that into a pistol or
///   its fists).
/// - **On the back** (<see cref="SetOnBack"/>) for a fist fight: the hands let go first.
///
/// EnemyBody drives it and reads <see cref="LeftHandWant"/> for the hand rig's weight.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public class EnemyRifle : MonoBehaviour
{
    public enum Hold { Held, Slung, Dropped, OnBack, Recovering, PickingUp, Lost, Stowing }

    [Header("Weight in the hands")]
    [Tooltip("How hard the rifle is pulled back to the hand's hold (a spring, per second squared).")]
    [Range(20f, 600f)] public float swayStiffness = 160f;
    [Tooltip("How quickly the rifle's swing dies away. Twice the square root of the stiffness is no overshoot.")]
    [Range(1f, 60f)] public float swayDamping = 19f;
    [Tooltip("Metres the rifle lags per metre-a-second-squared the hands accelerate.")]
    [Range(0f, 0.05f)] public float swayInertia = 0.006f;
    [Tooltip("Degrees the rifle lags per degree-a-second the body turns.")]
    [Range(0f, 0.1f)] public float swayTurnInertia = 0.025f;
    [Tooltip("The most it ever lags: metres, and degrees.")]
    public Vector2 swayLimit = new Vector2(0.04f, 7f);

    [Header("Sling")]
    [Tooltip("Metres of slack in the strap: how far the rifle can swing off the chest.")]
    [Range(0.02f, 0.5f)] public float slingSlack = 0.16f;
    [Tooltip("Degrees it can swing on the strap, every way.")]
    [Range(5f, 90f)] public float slingSwing = 35f;

    public Hold State { get; private set; } = Hold.Held;

    /// <summary>In the hands (or as good as): what the aim, the firing and the left hand read.</summary>
    public bool InHands => State == Hold.Held;

    /// <summary>How much the left hand wants the handguard, 0 to 1, before the body's own say.</summary>
    public float LeftHandWant { get; private set; } = 1f;

    /// <summary>Gone for good this life: dropped out of reach, or into the player's hands. Pistol or fists from here.</summary>
    public bool Lost => State == Hold.Lost;

    /// <summary>Walking to it, crouching for it or bringing it up: the mind waits.</summary>
    public bool Busy => State == Hold.PickingUp || State == Hold.Recovering || State == Hold.Stowing;

    /// <summary>Across the back and done moving (for a fist fight, or a sidearm).</summary>
    public bool Stowed => State == Hold.OnBack;

    EnemyBody _body;
    EnemyAI _ai;
    EnemyWounds _wounds;
    EnemyMotor _motor;
    Animator _animator;
    Transform _gun, _socket, _sway, _rightGrip;
    Transform _chest, _rUpper, _rLower, _rHand;
    Rigidbody _rb;
    List<Collider> _boxes;
    ConfigurableJoint _joint;

    Vector3 _off, _offVel, _turn, _turnVel;
    Vector3 _lastSocketPos, _lastSocketVel;
    Quaternion _lastSocketRot;
    bool _hasLast;

    // Getting it back: when, from where, how long.
    float _phaseStart, _recoverSeconds = 0.5f;
    int _phase;
    Vector3 _fromPos;
    Quaternion _fromRot;
    float _reach;     // the right hand's reach for it, 0 to 1
    float _giveUpAt;

    BodyDamageProfile Profile => _wounds != null ? _wounds.Body : null;

    void Awake()
    {
        _body = GetComponent<EnemyBody>();
        _ai = GetComponent<EnemyAI>();
        _wounds = GetComponent<EnemyWounds>();
        _motor = GetComponent<EnemyMotor>();
        _animator = GetComponentInChildren<Animator>();
        if (_body != null)
        {
            _gun = _body.gun;
            _socket = _body.rifleSocket;
            _sway = _body.rifleSway;
            _rightGrip = _body.rightGrip;
        }
        if (_animator != null && _animator.isHuman)
        {
            _chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? _animator.GetBoneTransform(HumanBodyBones.Chest);
            _rUpper = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rLower = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        if (_socket == null || _gun == null) enabled = false;
    }

    // ======================================================================
    // Held: the spring that gives it weight
    // ======================================================================

    /// <summary>
    /// Lays the rifle where it is carried: on the hand's sway when held, across the chest's back
    /// when slung there. It is never a child of the skeleton (a reparent makes the Animator
    /// rebind). EnemyBody calls this before it aims (to read the bore) and after its overlays.
    /// </summary>
    public void Follow()
    {
        if (!enabled) return;
        if (State == Hold.Held) _gun.SetPositionAndRotation(_sway.position, _sway.rotation);
        else if (State == Hold.OnBack && _chest != null)
        {
            BackPose(out Vector3 pos, out Quaternion rot);
            _gun.SetPositionAndRotation(_chest.TransformPoint(pos), _chest.rotation * rot);
        }
    }

    void Update()
    {
        if (State == Hold.Held || State == Hold.Recovering) Sway();
        if (State == Hold.PickingUp) PickUp();

        float want = State == Hold.Held ? 1f : 0f;
        if (State == Hold.Recovering) want = _phase == 1 ? Mathf.Clamp01((Time.time - _phaseStart) / (_recoverSeconds * 0.5f) - 0.4f) : 0f;
        if (_wounds != null && _wounds.AtLeast(BodyPart.LeftArm, EnemyWounds.LimbState.Crippled)) want = 0f;
        LeftHandWant = Mathf.MoveTowards(LeftHandWant, want, Time.deltaTime * 4f);
    }

    /// <summary>The hands' acceleration and turn, from last frame, drive a spring the rifle hangs on.</summary>
    void Sway()
    {
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        Vector3 pos = _socket.position;
        Quaternion rot = _socket.rotation;
        Vector3 target = Vector3.zero, turnTarget = Vector3.zero;
        if (_hasLast)
        {
            Vector3 vel = (pos - _lastSocketPos) / dt;
            Vector3 acc = (vel - _lastSocketVel) / dt;
            _lastSocketVel = vel;
            // Lags behind the way the hands are going; turns behind the way they turn.
            target = Vector3.ClampMagnitude(_socket.InverseTransformDirection(-acc) * swayInertia, swayLimit.x);
            (rot * Quaternion.Inverse(_lastSocketRot)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude > 0.5f)
            {
                Vector3 local = _socket.InverseTransformDirection(axis) * (-angle / dt * swayTurnInertia);
                turnTarget = Vector3.ClampMagnitude(local, swayLimit.y);
            }
        }
        _lastSocketPos = pos;
        _lastSocketRot = rot;
        _hasLast = true;

        // Critically-damped-ish springs towards the lag.
        _offVel += (swayStiffness * (target - _off) - swayDamping * _offVel) * dt;
        _off += _offVel * dt;
        _turnVel += (swayStiffness * (turnTarget - _turn) - swayDamping * _turnVel) * dt;
        _turn += _turnVel * dt;

        Vector3 extra = Vector3.zero;
        Quaternion extraRot = Quaternion.identity;
        Kick(ref extra, ref extraRot);

        _sway.localPosition = _off + extra;
        _sway.localRotation = extraRot * Quaternion.Euler(_turn);
    }

    /// <summary>Recoil, a body in pain letting the muzzle sag, and the butt stroke, on the rifle's own axes.</summary>
    void Kick(ref Vector3 extra, ref Quaternion turn)
    {
        if (_ai == null) return;
        float since = Time.time - _ai.LastShotTime;
        if (since >= 0f && since < 0.12f)
        {
            float k = 1f - since / 0.12f;
            extra += new Vector3(0f, 0.01f, -0.05f) * k;
            turn = Quaternion.Euler(-5f * k, 0f, 0f) * turn;
        }

        if (_ai.Attacking && _ai.target != null && Vector3.Distance(transform.position, _ai.target.position) <= _ai.meleeRange + 0.3f)
        {
            float t = Mathf.InverseLerp(_ai.WindupStart, _ai.WindupEnd, Time.time);
            extra += new Vector3(0.05f, 0.08f, -0.18f) * t;
            turn = Quaternion.Euler(-35f * t, 10f * t, 0f) * turn;
        }
        float swing = Time.time - _ai.LastMeleeTime;
        if (swing >= 0f && swing < 0.3f)
        {
            float k = Mathf.Sin(swing / 0.3f * Mathf.PI);
            extra += new Vector3(-0.05f, 0.05f, 0.35f) * k;
            turn = Quaternion.Euler(55f * k, 0f, 0f) * turn;
        }

        float dip = _body != null ? _body.GunDip : 0f;
        if (_wounds != null && _wounds.OneHanded) dip = Mathf.Max(dip, 0.45f);
        if (dip > 0.001f) turn = Quaternion.Euler(40f * dip, -15f * dip, 0f) * turn;
    }

    // ======================================================================
    // Falls
    // ======================================================================

    /// <summary>
    /// The body has been thrown or has fallen at this speed: the rifle goes onto the sling or
    /// out of the hands. Returns true if it was dropped.
    /// </summary>
    public bool OnFall(Vector3 velocity)
    {
        if (!enabled || State == Hold.Lost || State == Hold.Dropped || State == Hold.Slung) return State == Hold.Dropped;
        if (State == Hold.OnBack) return false;   // already across the back, strapped tight

        var p = Profile;
        float speed = velocity.magnitude;
        float drop = p != null ? Mathf.InverseLerp(p.slingBelowSpeed, Mathf.Max(p.slingBelowSpeed + 0.01f, p.dropAboveSpeed), speed) : 0.5f;
        bool dropped = Random.value < drop;

        Loose(velocity * 0.85f);
        if (dropped)
        {
            State = Hold.Dropped;
            _rb.angularDamping = 0.8f;
            _rb.angularVelocity = Random.insideUnitSphere * 5f;
        }
        else
        {
            State = Hold.Slung;
            Strap();
        }
        LeftHandWant = 0f;
        return dropped;
    }

    /// <summary>Lets the rifle go as a physics body of its own, at this velocity.</summary>
    void Loose(Vector3 velocity)
    {
        _gun.SetParent(null, true);
        _rb = _gun.GetComponent<Rigidbody>();
        if (_rb == null) _rb = _gun.gameObject.AddComponent<Rigidbody>();
        var p = Profile;
        _rb.mass = p != null ? p.rifleMass : 3.5f;
        _rb.linearDamping = 0.3f;
        _rb.angularDamping = 3f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Swept, not speculative: a thin hull landing on a terrain heightfield at a few metres a second
        // sank up to 42 mm into it with speculative contacts (VerifyGunDrop).
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rb.isKinematic = false;
        _rb.linearVelocity = velocity;

        if (_boxes == null)
        {
            _boxes = GunColliders.Fit(_gun);
            GunColliders.IgnoreBody(_boxes, transform);
        }
        GunColliders.Enable(_boxes, true);
        _rb.ResetCenterOfMass();
        _rb.ResetInertiaTensor();
        _rb.WakeUp();
    }

    /// <summary>The strap: a short, damped, tightly limited joint from the front of the chest.</summary>
    void Strap()
    {
        var chestBody = _chest != null ? _chest.GetComponent<Rigidbody>() : null;
        if (chestBody == null) { State = Hold.Dropped; return; }

        _joint = _gun.gameObject.AddComponent<ConfigurableJoint>();
        _joint.connectedBody = chestBody;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor = _gun.InverseTransformPoint(_rightGrip != null ? Vector3.Lerp(_rightGrip.position, _gun.position, 0.5f) : _gun.position);
        Vector3 strapAt = _chest.position + transform.forward * 0.16f * transform.lossyScale.y - transform.right * 0.05f;
        _joint.connectedAnchor = _chest.InverseTransformPoint(strapAt);
        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Limited;
        _joint.linearLimit = new SoftJointLimit { limit = slingSlack, bounciness = 0f, contactDistance = 0.01f };
        _joint.linearLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
        _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion = ConfigurableJointMotion.Limited;
        _joint.lowAngularXLimit = new SoftJointLimit { limit = -slingSwing };
        _joint.highAngularXLimit = new SoftJointLimit { limit = slingSwing };
        _joint.angularYLimit = new SoftJointLimit { limit = slingSwing };
        _joint.angularZLimit = new SoftJointLimit { limit = slingSwing };
        _joint.rotationDriveMode = RotationDriveMode.Slerp;
        _joint.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 6f, maximumForce = float.MaxValue };
        _joint.projectionMode = JointProjectionMode.PositionAndRotation;
        _joint.enablePreprocessing = false;
    }

    /// <summary>Out of the hands for good (the arms are gone, or it died): falls and stays.</summary>
    public void Drop(Vector3 push)
    {
        if (!enabled || State == Hold.Dropped || State == Hold.Lost) return;
        if (_joint != null) Destroy(_joint);
        Loose(push);
        _rb.angularDamping = 0.8f;
        _rb.angularVelocity = Random.insideUnitSphere * 3f;
        State = Hold.Lost;
        LeftHandWant = 0f;
    }

    // ======================================================================
    // Getting it back
    // ======================================================================

    /// <summary>Standing again: off the sling into the hands, or over to pick it up, or without.</summary>
    public void BeginRecover()
    {
        if (!enabled) return;
        var p = Profile;
        _recoverSeconds = p != null ? p.weaponRecoverSeconds : 0.5f;

        switch (State)
        {
            case Hold.Slung:
            case Hold.OnBack:
                StartBringUp();
                break;

            case Hold.Dropped:
                float reach = p != null ? p.pickUpReach : 5f;
                float keepOff = p != null ? p.pickUpPlayerDistance : 8f;
                bool near = Flat(_gun.position - transform.position).magnitude <= reach;
                bool safe = _ai == null || _ai.target == null || Flat(_ai.target.position - transform.position).magnitude >= keepOff;
                bool arms = _wounds == null || !_wounds.Disarmed;
                if (near && safe && arms && UnityEngine.AI.NavMesh.SamplePosition(_gun.position, out _, 1.2f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    State = Hold.PickingUp;
                    _phase = 0;
                    _phaseStart = Time.time;
                    _giveUpAt = Time.time + 7f;
                }
                else State = Hold.Lost;
                break;
        }
    }

    /// <summary>The right hand goes to it where it hangs, then brings it up to the ready.</summary>
    void StartBringUp()
    {
        if (_joint != null) Destroy(_joint);
        if (_rb != null) { _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; _rb.isKinematic = true; _rb.interpolation = RigidbodyInterpolation.None; }
        GunColliders.Enable(_boxes, false);
        if (_gun.parent != transform) _gun.SetParent(transform, true);
        _fromPos = transform.InverseTransformPoint(_gun.position);
        _fromRot = Quaternion.Inverse(transform.rotation) * _gun.rotation;
        State = Hold.Recovering;
        _phase = 0;
        _phaseStart = Time.time;
        _reach = 0f;
    }

    void PickUp()
    {
        if (_ai == null || Time.time > _giveUpAt) { Finish(false); return; }
        float distance = Flat(_gun.position - transform.position).magnitude;
        switch (_phase)
        {
            case 0:     // over to it
                if (distance > 0.75f) { _ai.MoveTo(_gun.position, 0.6f); return; }
                _ai.Hold();
                _phase = 1;
                _phaseStart = Time.time;
                if (_motor != null) _motor.ForceCrouch = true;
                return;
            case 1:     // down, and the hand to it
                if (Time.time - _phaseStart < 0.55f) return;
                StartBringUp();
                _recoverSeconds = Mathf.Max(_recoverSeconds, 0.6f);
                return;
        }
    }

    /// <summary>
    /// Off the sling (or the back) and into the hand at once: lying face down and firing from there
    /// (EnemyBody), the arms are the prone clip's and the rifle is laid in the hand by Follow. False
    /// when it is not on the body (dropped or lost).
    /// </summary>
    public bool TakeInHand()
    {
        if (State == Hold.Held) return true;
        if (State != Hold.Slung && State != Hold.OnBack && State != Hold.Recovering) return false;
        if (_joint != null) Destroy(_joint);
        if (_rb != null) { _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; _rb.isKinematic = true; _rb.interpolation = RigidbodyInterpolation.None; }
        GunColliders.Enable(_boxes, false);
        Finish(true);
        return true;
    }

    /// <summary>On the body (in the hand, on the sling or the back), not on the ground.</summary>
    public bool OnBody => State == Hold.Held || State == Hold.Slung || State == Hold.OnBack || State == Hold.Recovering;

    void Finish(bool held)
    {
        if (_motor != null) _motor.ForceCrouch = false;
        if (held)
        {
            if (_gun.parent != transform) _gun.SetParent(transform, true);
            _gun.SetPositionAndRotation(_sway.position, _sway.rotation);
            _off = _offVel = _turn = _turnVel = Vector3.zero;
            _hasLast = false;
            State = Hold.Held;
        }
        else State = Hold.Lost;
    }

    void LateUpdate()
    {
        if (State == Hold.Stowing) { Stow(); return; }
        if (State == Hold.PickingUp && _phase == 1)
        {
            // Crouched: the right hand reaches down to the grip.
            _reach = Mathf.Clamp01((Time.time - _phaseStart) / 0.45f);
            ReachFor(_rightGrip.position, Smooth(_reach));
            return;
        }
        if (State != Hold.Recovering) return;

        // First the hand goes to it; then it comes up into the hand's hold.
        float t = (Time.time - _phaseStart) / Mathf.Max(0.1f, _recoverSeconds);
        float reachShare = 0.4f;
        if (_phase == 0)
        {
            Vector3 at = transform.TransformPoint(_fromPos);
            _gun.SetPositionAndRotation(at, transform.rotation * _fromRot);
            ReachFor(_rightGrip.position, Smooth(t / reachShare));
            if (t >= reachShare)
            {
                _phase = 1;
                _phaseStart = Time.time;
                _fromPos = transform.InverseTransformPoint(_gun.position);
                _fromRot = Quaternion.Inverse(transform.rotation) * _gun.rotation;
                if (_motor != null) _motor.ForceCrouch = false;
            }
            return;
        }

        float up = Smooth(Mathf.Clamp01((Time.time - _phaseStart) / Mathf.Max(0.1f, _recoverSeconds * (1f - reachShare))));
        Vector3 from = transform.TransformPoint(_fromPos);
        Quaternion fromRot = transform.rotation * _fromRot;
        _gun.SetPositionAndRotation(Vector3.Lerp(from, _sway.position, up), Quaternion.Slerp(fromRot, _sway.rotation, up));
        // The hand stays on it while it comes up, and lets the hold take over as it arrives.
        ReachFor(_rightGrip.position, 1f - up);
        if (up >= 1f) Finish(true);
    }

    void ReachFor(Vector3 grip, float weight)
    {
        if (_rUpper == null || _rLower == null || _rHand == null || weight <= 0.001f) return;
        if (_wounds != null && _wounds.AtLeast(BodyPart.RightArm, EnemyWounds.LimbState.Crippled)) return;
        Quaternion u = _rUpper.rotation, l = _rLower.rotation, h = _rHand.rotation;
        LimbIK.Solve(_rUpper, _rLower, _rHand, grip, _rUpper.position + transform.TransformDirection(new Vector3(0.6f, -0.6f, -0.2f)));
        if (weight >= 0.999f) return;
        _rUpper.rotation = Quaternion.Slerp(u, _rUpper.rotation, weight);
        _rLower.rotation = Quaternion.Slerp(l, _rLower.rotation, weight);
        _rHand.rotation = Quaternion.Slerp(h, _rHand.rotation, weight);
    }

    /// <summary>
    /// For a fist fight or the sidearm: the rifle goes over the shoulder onto the back, carried
    /// there by the right hand over a moment; or back off the back into the hands.
    /// </summary>
    public void SetOnBack(bool onBack)
    {
        if (!enabled) return;
        if (onBack)
        {
            if (State != Hold.Held || _chest == null) return;
            _fromPos = _chest.InverseTransformPoint(_gun.position);
            _fromRot = Quaternion.Inverse(_chest.rotation) * _gun.rotation;
            _phaseStart = Time.time;
            State = Hold.Stowing;
            LeftHandWant = 0f;
        }
        else if (State == Hold.OnBack)
        {
            StartBringUp();
        }
    }

    const float StowSeconds = 0.45f;

    /// <summary>Where it rides on the back, in the chest's space: diagonal, muzzle down over the left shoulder.</summary>
    void BackPose(out Vector3 pos, out Quaternion rot)
    {
        Vector3 world = _chest.position - transform.forward * 0.2f * transform.lossyScale.y + transform.up * 0.05f;
        Quaternion worldRot = Quaternion.LookRotation(Quaternion.AngleAxis(40f, transform.forward) * transform.up, -transform.forward);
        pos = _chest.InverseTransformPoint(world);
        rot = Quaternion.Inverse(_chest.rotation) * worldRot;
    }

    void Stow()
    {
        float t = Smooth((Time.time - _phaseStart) / StowSeconds);
        BackPose(out Vector3 pos, out Quaternion rot);
        _gun.SetPositionAndRotation(_chest.TransformPoint(Vector3.Lerp(_fromPos, pos, t)), _chest.rotation * Quaternion.Slerp(_fromRot, rot, t));
        // The right hand carries it most of the way, then lets it go.
        ReachFor(_rightGrip.position, 1f - Smooth((Time.time - _phaseStart) / (StowSeconds * 0.7f)));
        if (t >= 1f) State = Hold.OnBack;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
