using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A car's body dented where it is hit hard (his note 2026-11-09, crashes "same as in GTA"): the panels round the point of impact are
/// pushed in, deepest at the point and fading out over a radius that grows with the blow. The first dent gives the car its own copy of
/// its body mesh (the shared one is every car of that kind); the far levels of detail are left as they are. Costs nothing until hit.
/// </summary>
[DisallowMultipleComponent]
public class CarDents : MonoBehaviour
{
    [Tooltip("Speed of impact, metres a second, under which nothing dents.")]
    [Min(0f)] public float from = 4f;
    [Tooltip("Deepest a dent goes, metres.")]
    [Min(0f)] public float maxDepth = 0.16f;

    readonly Dictionary<MeshFilter, Vector3[]> _own = new Dictionary<MeshFilter, Vector3[]>();
    MeshFilter[] _bodies;

    void OnCollisionEnter(Collision c)
    {
        float hard = c.relativeVelocity.magnitude;
        if (hard < from || c.contactCount == 0) return;
        Dent(c.GetContact(0).point, c.relativeVelocity, hard);
    }

    /// <summary>Pushes the body in at <paramref name="point"/>, along the blow, as deep as <paramref name="speed"/> makes it.</summary>
    public void Dent(Vector3 point, Vector3 blow, float speed)
    {
        _bodies ??= Bodies();
        float depth = Mathf.Clamp((speed - from) * 0.012f + 0.02f, 0.02f, maxDepth);
        float radius = Mathf.Clamp(0.25f + speed * 0.025f, 0.25f, 0.7f);
        // Into the car: from the point towards its middle, leaning along the blow.
        Vector3 into = ((transform.position + Vector3.up * 0.7f - point).normalized + blow.normalized * 0.5f).normalized;
        foreach (var mf in _bodies)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var r = mf.GetComponent<Renderer>();
            if (r == null || !r.bounds.Intersects(new Bounds(point, Vector3.one * radius * 2f))) continue;
            if (!_own.TryGetValue(mf, out var verts))
            {
                if (!mf.sharedMesh.isReadable) continue;
                var whole = mf.sharedMesh;
                var copy = Instantiate(whole);
                copy.name = whole.name + " (dented)";
                mf.sharedMesh = copy;
                // A collider that drew the same mesh follows it. (This compared the collider's mesh with the copy, which it never is.)
                if (mf.TryGetComponent<MeshCollider>(out var mc) && mc.sharedMesh == whole) mc.sharedMesh = copy;
                verts = copy.vertices;
                _own[mf] = verts;
            }
            var t = mf.transform;
            Vector3 p = t.InverseTransformPoint(point), dir = t.InverseTransformDirection(into);
            float r2 = radius / Mathf.Max(0.01f, t.lossyScale.x);
            float d = depth / Mathf.Max(0.01f, t.lossyScale.x);
            bool moved = false;
            for (int i = 0; i < verts.Length; i++)
            {
                float dist = (verts[i] - p).magnitude;
                if (dist >= r2) continue;
                float k = 1f - dist / r2;
                verts[i] += dir * d * k * k;
                moved = true;
            }
            if (!moved) continue;
            var mesh = mf.sharedMesh;
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    /// <summary>The dented copies go with the car: each crashed car that was put away left its meshes behind until the level ended.</summary>
    void OnDestroy()
    {
        foreach (var mf in _own.Keys) if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
    }

    /// <summary>The body's meshes: every readable mesh of the car but its wheels and the first level of detail only.</summary>
    MeshFilter[] Bodies()
    {
        var list = new List<MeshFilter>();
        var lodGroup = GetComponentInChildren<LODGroup>();
        var far = new HashSet<Renderer>();
        if (lodGroup != null)
        {
            var lods = lodGroup.GetLODs();
            for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) far.Add(r);
        }
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.name.ToLowerInvariant();
            if (n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire")) continue;
            var r = mf.GetComponent<Renderer>();
            // Not the person at the wheel (the car itself may carry a Health of its own: it is still its body).
            if (r == null || far.Contains(r) || r.GetComponentInParent<VehicleDriver>() != null) continue;
            list.Add(mf);
        }
        return list.ToArray();
    }
}
