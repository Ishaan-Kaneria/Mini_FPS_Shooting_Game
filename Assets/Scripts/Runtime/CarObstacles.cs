using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Nobody walks through a car (his note 2026-11-09, "cars natural and solid, same as in GTA"). People and soldiers go where the navmesh
/// takes them, and their bodies do not meet a car as a solid (DrivableCar.PeopleDoNotCollide: a soldier pressed into one threw it over),
/// so the navmesh itself has to keep them off: every car is a NavMeshObstacle the size of its body. Parked cars are cut out of the
/// navmesh where they stand; traffic and drivable cars are cut out whenever they stop, and avoided as they move.
///
/// Added when an arena's scene loads (to every car found) and by each car as it is made (<see cref="Add"/>). Holds no state.
/// </summary>
public static class CarObstacles
{
    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_hooked) SceneManager.sceneLoaded -= OnLoaded;
        _hooked = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!_hooked) { SceneManager.sceneLoaded += OnLoaded; _hooked = true; }
        OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "ParkedCars")
                    foreach (Transform car in t)
                    {
                        Add(car.gameObject, false);
                        DrivableCar.RegisterParked(car);                      // any of them can be taken and driven
                        if (car.GetComponent<CarWreck>() == null) car.gameObject.AddComponent<CarWreck>();   // and shot up and burnt
                    }
            foreach (var d in root.GetComponentsInChildren<DrivableCar>(true)) Add(d.gameObject, true);
        }
    }

    /// <summary>A car made an obstacle the size of its colliders (or its renderers). <paramref name="moves"/>: cut out only while it stands.</summary>
    public static void Add(GameObject car, bool moves)
    {
        if (car == null || car.GetComponent<NavMeshObstacle>() != null) return;
        var t = car.transform;
        Bounds local = default; bool any = false;
        void Grow(Bounds world)
        {
            // The world box's corners into the car's space.
            for (int i = 0; i < 8; i++)
            {
                var c = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = t.InverseTransformPoint(c);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
            }
        }
        foreach (var col in car.GetComponentsInChildren<Collider>(true))
            if (!col.isTrigger && !(col is WheelCollider) && col.GetComponentInParent<Health>() == null) Grow(col.bounds);
        if (!any)
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true)) Grow(r.bounds);
        if (!any || local.size.x < 0.5f || local.size.z < 0.5f) return;
        var o = car.AddComponent<NavMeshObstacle>();
        o.shape = NavMeshObstacleShape.Box;
        o.center = new Vector3(local.center.x, local.center.y, local.center.z);
        // A little wider than the body, so a shoulder does not pass through a wing mirror.
        o.size = new Vector3(local.size.x + 0.2f, Mathf.Max(1f, local.size.y), local.size.z + 0.2f);
        o.carving = true;
        o.carveOnlyStationary = moves;
        o.carvingMoveThreshold = 0.2f;
        o.carvingTimeToStationary = 0.6f;
    }
}
