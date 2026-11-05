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
        if (_fitted || _animator == null || !_animator.isHuman || ++_fitFrames < 3) return;
        _fitted = true;
        var head = _animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (head == null || hips == null || transform.parent == null) return;
        float roof = float.MinValue;
        foreach (var r in transform.parent.GetComponentsInChildren<MeshRenderer>())
            if (!r.transform.IsChildOf(transform)) roof = Mathf.Max(roof, r.bounds.max.y);
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
        if (CivilianCasualties.ByPlayer(h.LastDamage.source)) CivilianCasualties.Report();
        Died?.Invoke(this);
    }
}
