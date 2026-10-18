using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colliders a gun lies on once it is out of a pair of hands: an enemy's rifle let go in a
/// fall or a death (<see cref="EnemyRifle"/>, <see cref="EnemyBody"/>), and the player's own gun
/// when it is dropped empty.
///
/// It used to be one box round all of the gun's meshes, which is the gun's outline only seen
/// straight down its three axes: on its right side the AK lay on the plane of its bolt handle,
/// which sticks out 2 cm past the receiver, so the whole rifle hung 2 cm over the ground on a
/// shelf nobody could see (and the plain path's fixed box was worse: 15 mm up on its side, the
/// magazine 11 cm into the ground upright). Boxes per part were still wrong in kind: a box lies on
/// a whole face where the gun's surface touches only along its ridges (measured: the body 11 mm up,
/// the centre of mass 66 mm outside where the drawn gun touched). So every visible part gets convex
/// hulls of its own vertices, a long part (the receiver, barrel and stock in one mesh) cut into
/// lengths first so the hulls follow its profile: a hull rests on exactly the points the gun would.
/// The rigidbody takes its centre of mass from them. A part whose mesh cannot be read gets a box
/// from its bounds instead. VerifyGunDrop measures all of it.
/// </summary>
public static class GunColliders
{
    /// <summary>Metres of gun each box covers along a long part.</summary>
    const float SliceLength = 0.1f;
    const int MaxSlices = 10;
    /// <summary>A part smaller than this in every direction (a trigger) is left to the others.</summary>
    const float Smallest = 0.03f;

    static PhysicsMaterial _grippy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _grippy = null;

    /// <summary>A grippy, dead material: a gun lands, rolls a little and stays.</summary>
    public static PhysicsMaterial Grippy => _grippy != null ? _grippy : _grippy = new PhysicsMaterial("DroppedGun")
    {
        dynamicFriction = 0.85f,
        staticFriction = 1f,
        bounciness = 0.05f,
        frictionCombine = PhysicsMaterialCombine.Maximum,
        bounceCombine = PhysicsMaterialCombine.Minimum,
    };

    /// <summary>
    /// Adds the fitted colliders under <paramref name="gun"/> (on each part, so they turn with it)
    /// and returns them.
    /// </summary>
    public static List<Collider> Fit(Transform gun)
    {
        var made = new List<Collider>();
        foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<Renderer>();
            var mesh = mf.sharedMesh;
            if (r == null || !r.enabled || mesh == null) continue;

            Vector3 size = Vector3.Scale(mesh.bounds.size, Abs(mf.transform.lossyScale));
            if (size.x < Smallest && size.y < Smallest && size.z < Smallest) continue;

            if (!mesh.isReadable) { made.Add(Box(mf.gameObject, mesh.bounds)); continue; }

            // Cut along the part's longest axis, in its own space.
            var b = mesh.bounds;
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
            float length = size[axis];
            int slices = Mathf.Clamp(Mathf.RoundToInt(length / SliceLength), 1, MaxSlices);

            // Each triangle goes to every length it reaches into, so neighbouring hulls share the
            // triangles across the cut and meet with no gap for anything to catch on.
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var points = new List<Vector3>[slices];
            float start = b.min[axis], step = b.size[axis] / slices;
            int Slice(float x) => step > 0f ? Mathf.Clamp((int)((x - start) / step), 0, slices - 1) : 0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], c = vertices[triangles[t + 1]], d = vertices[triangles[t + 2]];
                int from = Slice(Mathf.Min(a[axis], Mathf.Min(c[axis], d[axis])));
                int to = Slice(Mathf.Max(a[axis], Mathf.Max(c[axis], d[axis])));
                for (int i = from; i <= to; i++)
                {
                    var list = points[i] ??= new List<Vector3>();
                    list.Add(a); list.Add(c); list.Add(d);
                }
            }
            for (int i = 0; i < slices; i++)
            {
                var hull = points[i] != null && points[i].Count >= 4 ? Hull(mf.gameObject, points[i]) : null;
                if (hull != null) made.Add(hull);
            }
        }
        foreach (var c in made) c.sharedMaterial = Grippy;
        return made;
    }

    /// <summary>A convex collider round these points, in the part's own space; null if it will not cook.</summary>
    static Collider Hull(GameObject on, List<Vector3> points)
    {
        var mesh = new Mesh { name = on.name + " drop hull" };
        mesh.SetVertices(points);
        var index = new int[points.Count - points.Count % 3];
        for (int i = 0; i < index.Length; i++) index[i] = i;
        mesh.SetTriangles(index, 0);
        var collider = on.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = mesh;
        if (collider.sharedMesh == null) { Object.Destroy(collider); Object.Destroy(mesh); return null; }
        return collider;
    }

    static BoxCollider Box(GameObject on, Bounds local)
    {
        var box = on.AddComponent<BoxCollider>();
        box.center = local.center;
        // A box under 6 mm thick can let a fast gun through a seam; none of the AK's parts is.
        box.size = Vector3.Max(local.size, Vector3.one * 0.006f);
        return box;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>Switches them on or off together: off while the gun is in the hands or on the sling's joint.</summary>
    public static void Enable(List<Collider> colliders, bool on)
    {
        if (colliders == null) return;
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    /// <summary>The gun's boxes never touch the body that carried it.</summary>
    public static void IgnoreBody(List<Collider> colliders, Transform body)
    {
        if (colliders == null || body == null) return;
        foreach (var c in body.GetComponentsInChildren<Collider>())
        {
            if (c == null || colliders.Contains(c)) continue;
            foreach (var g in colliders) if (g != null) Physics.IgnoreCollision(g, c);
        }
    }
}
