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
        var src = h.LastDamage.source;
        bool byPlayer = src != null && (src.CompareTag("Player") || src.GetComponentInParent<PlayerMotor>() != null);
        if (byPlayer) CivilianCasualties.Report();
        Died?.Invoke(this);
    }
}
