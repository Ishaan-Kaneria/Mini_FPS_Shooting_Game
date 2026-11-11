using UnityEngine;

/// <summary>
/// The person at the wheel of a moving vehicle (Docs/DESERT_VEHICLES.md). A civilian model in the driver's seat: its
/// animator in the driving pose (the "Activity" state Drive, else sitting), its body a short capsule that rounds hit
/// through the windscreen or a side window (the car's own collider stops at the waistline and starts again at the
/// roof). Killed, he slumps where he sits and the vehicle he drove rolls to a stop (<see cref="Died"/>).
///
/// Made in play from a civilian prefab with everything that walks taken off (the agent, the Civilian's routine), never
/// saved. A driver killed by the player counts as a civilian casualty; one killed by anyone else does not.
/// </summary>
[DisallowMultipleComponent]
public class VehicleDriver : MonoBehaviour
{
    static readonly int ActivityId = Animator.StringToHash("Activity");
    static readonly int SpeedId = Animator.StringToHash("Speed");

    public Health Health { get; private set; }
    public bool Dead => Health == null || Health.IsDead;

    /// <summary>The person he was seated from: who gets out of the car and runs.</summary>
    public GameObject Source { get; private set; }

    /// <summary>Raised once when he dies.</summary>
    public event System.Action<VehicleDriver> Died;

    Animator _animator;

    /// <summary>
    /// Seats a driver from <paramref name="prefab"/> in <paramref name="vehicle"/>, at <paramref name="seatLocal"/> in its
    /// space, facing its forward. Returns null if the prefab is not a person.
    /// </summary>
    public static VehicleDriver Seat(Transform vehicle, GameObject prefab, Vector3 seatLocal)
    {
        if (vehicle == null || prefab == null) return null;
        // Made under an inactive holder so nothing on it wakes (no agent looking for the navmesh, no Civilian routine)
        // before it is stripped.
        var holder = new GameObject("DriverHolder");
        holder.SetActive(false);
        var go = Instantiate(prefab, holder.transform);
        go.name = "Driver";
        foreach (var life in go.GetComponents<CivilianLife>()) DestroyImmediate(life);
        foreach (var civ in go.GetComponents<Civilian>()) DestroyImmediate(civ);
        foreach (var agent in go.GetComponents<UnityEngine.AI.NavMeshAgent>()) DestroyImmediate(agent);
        foreach (var obstacle in go.GetComponents<UnityEngine.AI.NavMeshObstacle>()) DestroyImmediate(obstacle);
        go.transform.SetParent(vehicle, false);
        go.transform.localPosition = seatLocal;
        go.transform.localRotation = Quaternion.identity;
        Destroy(holder);

        // A seated body: the capsule from the seat to the top of the head.
        var cap = go.GetComponent<CapsuleCollider>();
        if (cap != null) { cap.radius = 0.24f; cap.height = 1.15f; cap.center = new Vector3(0f, 0.75f, 0.05f); }
        var driver = go.AddComponent<VehicleDriver>();
        driver.Source = prefab;
        go.SetActive(true);
        return driver;
    }

    void Awake()
    {
        Health = GetComponent<Health>();
        if (Health != null) Health.destroyOnDeath = false;
        _animator = GetComponentInChildren<Animator>();
        // His bones not written while nobody sees him (traffic far off or behind the player). Not culled completely: then the state
        // machine stood still too, and a driver coming into view was still standing, then sat down (the car test, 2026-11-09).
        if (_animator != null) _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        _skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _look = GetComponentInParent<CarLook>();
        Legs(false);
    }

    /// <summary>
    /// His legs while he sits: folded away into his hips. The sitting clip stretches them further than a car's footwell, and they came out
    /// through the doors and the floor (his note 2026-11-09, "the people legs are outside the car"); the doors and the dash hide where they
    /// would be. Back the moment he gets out.
    /// </summary>
    void Legs(bool shown)
    {
        if (_animator == null || !_animator.isHuman) return;
        foreach (var b in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
        {
            var t = _animator.GetBoneTransform(b);
            if (t != null) t.localScale = shown ? Vector3.one : Vector3.one * 0.001f;
        }
    }

    SkinnedMeshRenderer[] _skins;
    CarLook _look;
    float _glance, _glanceUntil;

    /// <summary>Getting out (TrafficCar's sequence moves him): nothing here holds him to the wheel.</summary>
    public bool Leaving { get; private set; }

    /// <summary>Off the wheel and out of the seat: standing up as he goes (the standing pose), moved by the car's sequence.</summary>
    public void StartLeaving()
    {
        Leaving = true;
        Legs(true);
        if (_animator == null) return;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.SetInteger(ActivityId, (int)LifeActivity.None);
        _animator.SetFloat(SpeedId, 0f);
    }

    /// <summary>How far the car's steering wheel is turned, degrees (set by the car each frame): his hands follow it.</summary>
    public float WheelTurn { get; set; }

    /// <summary>Posed whether seen or not (a test's own camera does not count as seen).</summary>
    public static bool AlwaysPose;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AlwaysPose = false;

    bool Seen()
    {
        if (AlwaysPose || _skins == null) return true;
        foreach (var s in _skins) if (s != null && s.isVisible) return true;
        return false;
    }

    void OnEnable()
    {
        if (Health != null) Health.Died += OnDied;
        Pose();
    }

    void OnDisable()
    {
        if (Health != null) Health.Died -= OnDied;
    }

    bool _fitted;
    int _fitFrames;

    /// <summary>
    /// Once the seated pose has reached the bones: the body lowered to sit under this car's roof, and its capsule laid from hips to head.
    /// The sitting clip holds the hips over a metre up and the builder's seat is a guess, so the head came out through the roof and rounds
    /// through the windows passed under nothing (R11, R12). Measured here, it fits whatever car he is in.
    /// </summary>
    void LateUpdate()
    {
        if (Leaving || _animator == null || !_animator.isHuman || !Seen()) return;
        if (_fitted) { Drive(); return; }
        if (++_fitFrames < 3) return;
        _fitted = true;
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (head == null || hips == null || transform.parent == null) return;
        // The roof: the car's own collider straight over his hips (its roof box). The highest of the car's meshes, as before, took a
        // taxi sign or a tall level of detail for the roof, and in a low sports car he sat with his head and shoulders out of it (2026-11-09).
        float roof = float.MinValue;
        var car = transform.parent;
        foreach (var h in Physics.RaycastAll(hips.position, car.up, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform.IsChildOf(car) && !h.collider.transform.IsChildOf(transform)) roof = roof == float.MinValue ? h.point.y : Mathf.Min(roof, h.point.y);
        if (roof == float.MinValue)
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
                if (!r.transform.IsChildOf(transform) && r.GetComponentInParent<LODGroup>() == null) roof = Mathf.Max(roof, r.bounds.max.y);
        if (roof > float.MinValue)
        {
            float over = head.position.y + HeadTop + RoofGap - roof;
            if (over > 0f) transform.position += Vector3.down * over;
        }
        var cap = GetComponent<CapsuleCollider>();
        if (cap != null)
        {
            Vector3 low = transform.InverseTransformPoint(hips.position), high = transform.InverseTransformPoint(head.position);
            cap.direction = 1;
            cap.radius = 0.24f;
            cap.center = new Vector3(low.x, (low.y + high.y + HeadTop) * 0.5f, (low.z + high.z) * 0.5f);
            cap.height = high.y + HeadTop - low.y + 0.3f;
        }
    }

    const float HeadTop = 0.16f, RoofGap = 0.06f;

    /// <summary>
    /// At the wheel, not just sitting (his note 2026-11-09, "the person sitting in it is artificial"): both hands on the rim at ten and
    /// two, turning with it, elbows down and out; the head on the road ahead, turning into a bend, now and then a glance aside.
    /// Over the sitting clip, after it, every frame he is seen.
    /// </summary>
    void Drive()
    {
        if (Dead) return;
        var car = _look != null ? _look.transform : transform.parent;
        if (car == null) return;
        bool modelled = _look != null && _look.steerWheel != null;
        // A car with no wheel of its own modelled (the hand-made ones): one where a wheel is, in front of his chest.
        var lS = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); var rS = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Vector3 chest = lS != null && rS != null ? (lS.position + rS.position) * 0.5f : transform.position + car.up * 1.1f;
        Vector3 fakeHub = chest + car.forward * 0.42f - car.up * 0.2f;
        Vector3 fakeAxis = (car.forward * 0.85f - car.up * 0.5f).normalized;
        foreach (float side in new[] { -1f, 1f })
        {
            var upper = _animator.GetBoneTransform(side < 0f ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = _animator.GetBoneTransform(side < 0f ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = _animator.GetBoneTransform(side < 0f ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null) continue;
            Vector3 grip, hub;
            if (modelled) grip = _look.GripPoint(side, WheelTurn, out _, out hub);
            else
            {
                hub = fakeHub;
                Vector3 across = Vector3.ProjectOnPlane(car.right, fakeAxis).normalized, upOnWheel = Vector3.Cross(fakeAxis, across);
                if (Vector3.Dot(upOnWheel, car.up) < 0f) upOnWheel = -upOnWheel;
                Vector3 at = (across * side * 0.985f + upOnWheel * 0.17f) * 0.19f;
                grip = hub + Quaternion.AngleAxis(Mathf.Clamp(WheelTurn, -70f, 70f), fakeAxis) * at;
            }
            // The wrist a hand's length short of the rim, on the driver's side of it.
            Vector3 wrist = grip - (grip - hub).normalized * 0.02f;
            Vector3 hint = upper.position - car.up * 0.45f + car.right * side * 0.3f - car.forward * 0.1f;
            TwoBone(upper, lower, hand, wrist, hint);
            // The palm over the rim: the hand along the forearm, turned so its back faces up and out.
            Vector3 along = (grip - lower.position).normalized;
            hand.rotation = Quaternion.FromToRotation(hand.position - lower.position, along) * hand.rotation;
        }
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (head != null)
        {
            if (Time.time >= _glanceUntil) { _glance = Random.value < 0.25f ? Random.Range(-35f, 35f) : 0f; _glanceUntil = Time.time + Random.Range(1.5f, 4f); }
            float yaw = Mathf.Clamp(WheelTurn * 0.25f, -30f, 30f) + _glance;
            var want = Quaternion.LookRotation(Quaternion.AngleAxis(yaw, car.up) * car.forward, car.up);
            // Half of it from the neck: the head's own pose from the clip is kept underneath.
            var fromClip = head.rotation;
            var target = want * Quaternion.Inverse(Quaternion.LookRotation(car.forward, car.up)) * fromClip;
            head.rotation = Quaternion.Slerp(fromClip, target, 0.8f);
        }
    }

    /// <summary>A two-bone reach (shoulder, elbow, wrist) to <paramref name="target"/>, the elbow bent towards <paramref name="hint"/>.</summary>
    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        float ab = (b.position - a.position).magnitude, bc = (c.position - b.position).magnitude;
        Vector3 at = target - a.position;
        float d = Mathf.Clamp(at.magnitude, 0.05f, ab + bc - 0.002f);
        Vector3 dir = at.normalized;
        float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
        Vector3 n = Vector3.Cross(dir, hint - a.position);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.up);
        Vector3 bend = Vector3.Cross(n.normalized, dir).normalized;
        Vector3 elbow = a.position + (dir * cosA + bend * Mathf.Sqrt(1f - cosA * cosA)) * ab;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + dir * d - b.position) * b.rotation;
    }

    void Pose()
    {
        if (_animator == null) return;
        _animator.applyRootMotion = false;
        _animator.SetFloat(SpeedId, 0f);
        _animator.SetInteger(ActivityId, (int)LifeActivity.Drive);
    }

    void OnDied(Health h)
    {
        // Slumped in the seat: the pose stops where it is and the body tips toward the door. (A dead-at-the-wheel clip
        // would be better: Docs/DESERT_VEHICLES.md.)
        if (_animator != null) _animator.speed = 0f;
        transform.localRotation = Quaternion.Euler(8f, 0f, -18f);
        if (CivilianCasualties.ByPlayer(h.LastDamage.source))
        {
            CivilianCasualties.Report();
            WantedLevel.Report(WantedLevel.Crime.KilledCivilian, transform.position);
        }
        Died?.Invoke(this);
    }
}
