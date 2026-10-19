using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gun lying on the ground that the player can pick up (fists when out of ammo, Ishaan's Part B):
/// the player's own, dropped when it ran dry, or an enemy's rifle, dropped when it died or lost its
/// arms. Knows which gun it is (<see cref="WeaponData"/>) and the rounds still in it.
///
/// The player's own is spawned by <see cref="SpawnDropped"/> as a real physics object, shaped by
/// <see cref="GunColliders"/> so it lies on the ground (Part A2). An enemy's rifle is already one
/// (<see cref="EnemyRifle"/>, <see cref="EnemyBody"/>); it is marked with <see cref="MarkEnemyRifle"/>,
/// which works out which of the store's guns it is from its meshes and how many rounds the enemy had
/// left. <see cref="GunPickup"/> on the player finds the nearest one and takes it.
/// </summary>
[DisallowMultipleComponent]
public class GroundGun : MonoBehaviour
{
    [Tooltip("Which gun this is.")]
    public WeaponData data;

    [Tooltip("Rounds in its magazine, and carried with it.")]
    [Min(0)] public int magazine, reserve;

    [Tooltip("The player dropped it (their own, out of ammo): an ammo pickup re-arms them with it when it is near.")]
    public bool playersOwn;

    // Set in code (never serialized): whether it can be taken now, and what taking it does to whatever owned it.
    System.Func<bool> _available;
    System.Action _taken;

    static readonly List<GroundGun> _all = new List<GroundGun>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _all.Clear();
        _byMesh.Clear();
    }

    /// <summary>Every gun on the ground in the loaded scenes.</summary>
    public static IReadOnlyList<GroundGun> All => _all;

    void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
    void OnDisable() => _all.Remove(this);

    /// <summary>On the ground and free to take (an enemy's, not while it is still in a hand or on a sling).</summary>
    public bool Available => isActiveAndEnabled && data != null && (_available == null || _available());

    /// <summary>The middle of the gun, where the prompt and the reach are measured from.</summary>
    public Vector3 Centre
    {
        get
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : transform.position;
        }
    }

    /// <summary>Taken by the player: gone from the ground (an enemy's rifle is lost to its owner for good).</summary>
    public void Take()
    {
        if (_taken != null) _taken();
        else Destroy(gameObject);
    }

    // ======================================================================

    /// <summary>
    /// The player's gun, out of their hands as a real object: its model (the gun's view model, without the
    /// arms), on the default layer, shaped to its parts and thrown at <paramref name="velocity"/>.
    /// </summary>
    public static GroundGun SpawnDropped(WeaponData data, int magazine, int reserve, Vector3 position, Quaternion rotation,
                                         Vector3 velocity, bool playersOwn)
    {
        if (data == null) return null;
        GameObject go;
        if (data.viewModel != null)
        {
            go = Instantiate(data.viewModel, position, rotation);
            foreach (var spec in go.GetComponentsInChildren<ViewModelSpec>(true)) Destroy(spec);
            foreach (var anim in go.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = new Vector3(0.06f, 0.18f, 0.8f);
            Destroy(go.GetComponent<Collider>());
        }
        go.name = "Dropped " + data.weaponName;
        int layer = LayerMask.NameToLayer("Default");
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        var parts = GunColliders.Fit(go.transform);
        GunColliders.Enable(parts, true);
        var body = go.AddComponent<Rigidbody>();
        body.mass = 3.5f;
        body.linearDamping = 0.3f;
        body.angularDamping = 0.8f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.ResetCenterOfMass();
        body.ResetInertiaTensor();
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 2f;

        var ground = go.AddComponent<GroundGun>();
        ground.data = data;
        ground.magazine = Mathf.Max(0, magazine);
        ground.reserve = Mathf.Max(0, reserve);
        ground.playersOwn = playersOwn;
        return ground;
    }

    /// <summary>
    /// An enemy's rifle, now on the ground (dropped, or lost with its owner): which of the store's guns it is
    /// and the rounds left in it. <paramref name="available"/> says when it may be taken, <paramref name="taken"/>
    /// what that does to its owner. Nothing when the rifle is not a gun the store knows.
    /// </summary>
    public static GroundGun MarkEnemyRifle(GameObject rifle, int roundsLeft, System.Func<bool> available, System.Action taken)
    {
        if (rifle == null) return null;
        var data = DataFor(rifle.transform);
        if (data == null) return null;
        var ground = rifle.GetComponent<GroundGun>();
        if (ground == null) ground = rifle.AddComponent<GroundGun>();
        ground.data = data;
        int size = Mathf.Max(1, data.magazineSize);
        int left = Mathf.Max(0, roundsLeft);
        ground.magazine = Mathf.Min(size, left);
        ground.reserve = left - ground.magazine;
        ground.playersOwn = false;
        ground._available = available;
        ground._taken = taken;
        return ground;
    }

    /// <summary>
    /// Rounds an enemy's rifle starts with: a full magazine and two more, the load a rifleman carries. What it
    /// fires comes off it (EnemyAI.RoundsFired), so a rifle picked up after a long fight has less in it.
    /// </summary>
    public static int EnemyLoad(WeaponData data) => data == null ? 0 : Mathf.Max(1, data.magazineSize) * 3;

    // ---- which gun a model is ------------------------------------------------

    static readonly Dictionary<Mesh, WeaponData> _byMesh = new Dictionary<Mesh, WeaponData>();

    /// <summary>
    /// The store's gun whose view model shares a mesh with <paramref name="model"/> (the enemies carry the same
    /// models the player does), or null. Read from the scene's catalog (PlayerLoadout) and cached by mesh.
    /// </summary>
    public static WeaponData DataFor(Transform model)
    {
        var meshes = new List<Mesh>();
        foreach (var f in model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) meshes.Add(f.sharedMesh);
        foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh != null) meshes.Add(s.sharedMesh);
        foreach (var m in meshes) if (_byMesh.TryGetValue(m, out var known)) return known;

        var loadout = FindAnyObjectByType<PlayerLoadout>();
        var catalog = loadout != null ? loadout.catalog : null;
        if (catalog == null) return null;
        foreach (var entry in catalog.guns)
        {
            if (entry == null || entry.data == null || entry.data.viewModel == null) continue;
            var theirs = new HashSet<Mesh>();
            foreach (var f in entry.data.viewModel.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) theirs.Add(f.sharedMesh);
            foreach (var s in entry.data.viewModel.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh != null) theirs.Add(s.sharedMesh);
            foreach (var m in meshes)
                if (theirs.Contains(m)) { _byMesh[m] = entry.data; return entry.data; }
        }
        return null;
    }
}
