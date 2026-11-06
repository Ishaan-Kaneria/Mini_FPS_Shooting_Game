using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A car's windows breaking (Docs/DESIGN_NOTES.md "Glass that breaks like glass"). A round is tested against the windows' real triangles, so it is the window it went through
/// that breaks, where it went through (the old test was the nearest of six bounding boxes, and shots broke the wrong window). Side and rear glass is tempered: the first
/// hit takes the whole window out in cubes, in the window's shape. The windscreen is laminated: rounds star it, each web lying on the curve of the glass, stopping at the frame and at earlier cracks,
/// and it only caves in after many. No collider, trigger or update: costs nothing until hit.
/// </summary>
[DisallowMultipleComponent]
public class CarGlass : MonoBehaviour
{
    [System.Serializable]
    public class Window
    {
        public string name;
        [Tooltip("Whose space the glass is in: it moves with a door.")]
        public Transform frame;
        public MeshFilter filter;
        [Tooltip("Its triangles in the mesh's glass submesh. On a car with separate windows, all of it.")]
        public int submesh, start, count;
        public bool windscreen;
        [System.NonSerialized] public bool gone;
        [System.NonSerialized] public int hits;
        [System.NonSerialized] public GlassSurface surface;
        [System.NonSerialized] public CrackMesh cracks;
        [System.NonSerialized] public Vector3[] points;
        [System.NonSerialized] public int[] tris;
    }

    [Tooltip("Written by the builder. A pack car left without any falls back to its materials: one glass surface that goes all at once.")]
    public Window[] windows;
    [Tooltip("Lower levels of detail, whose glass is one slot: cleared once any window has gone.")]
    public Renderer[] farRenderers;
    public int farGlassSlot = 2;

    /// <summary>Rounds into the windscreen before it caves in. Laminated glass holds together: that is what it is for.</summary>
    const int WindscreenLasts = 7;

    readonly List<(Renderer r, int slot)> _merged = new List<(Renderer, int)>();
    bool _ready, _mergedGone, _farCleared;
    Bounds _local;
    bool _hasGlass;
    readonly Dictionary<Mesh, Mesh> _copies = new Dictionary<Mesh, Mesh>();

    static readonly HashSet<CarGlass> _all = new HashSet<CarGlass>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _all.Clear();

    void OnEnable() => _all.Add(this);
    void OnDisable() => _all.Remove(this);

    static bool IsGlass(Material m)
    {
        if (m == null) return false;
        string n = m.name;
        return n.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Windows", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void Setup()
    {
        if (_ready) return;
        _ready = true;
        bool any = false;
        var box = new Bounds();
        void Grow(Bounds world)
        {
            var c = transform.InverseTransformPoint(world.center);
            var e = world.extents;
            float reach = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 1.75f;
            var b = new Bounds(c, Vector3.one * reach * 2f);
            if (!any) { box = b; any = true; } else box.Encapsulate(b);
        }
        if (windows != null && windows.Length > 0)
            foreach (var w in windows)
            {
                if (w.filter == null) continue;
                var r = w.filter.GetComponent<Renderer>();
                if (r != null) Grow(r.bounds);
            }
        else
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    if (!IsGlass(mats[s])) continue;
                    _merged.Add((r, s));
                    Grow(r.bounds);
                }
            }
        _local = box; _hasGlass = any;
    }

    /// <summary>Every car's windows a shot's line goes through, its own too: a round fired from the driver's seat goes out through the glass.</summary>
    public static void Ray(Vector3 from, Vector3 dir, float distance, GlassField.Blow blow)
    {
        float reach = distance + 0.05f;
        foreach (var car in _all)
        {
            if (car == null) continue;
            car.Setup();
            if (!car._hasGlass) continue;
            // In the car's own space, a box test first: there can be three hundred cars.
            var t = car.transform;
            if (!car._local.IntersectRay(new Ray(t.InverseTransformPoint(from), t.InverseTransformDirection(dir)), out float near) || near > reach) continue;
            car.Trace(from, dir, reach, blow);
        }
    }

    public static void Blast(Vector3 centre, float radius)
    {
        float r2 = radius * radius;
        foreach (var car in _all)
        {
            if (car == null || (car.transform.position - centre).sqrMagnitude > r2) continue;
            car.Setup();
            var away = (car.transform.position - centre).normalized;
            if (car.windows != null)
                foreach (var w in car.windows) car.Burst(w, away);
            car.BreakMerged(away);
        }
    }

    /// <summary>A crash stars the screen at the point nearest the blow; a hard one takes the side glass too.</summary>
    public void Crash(Vector3 at, float speed)
    {
        Setup();
        if (windows == null) return;
        foreach (var w in windows)
        {
            if (w.gone || w.filter == null) continue;
            if (w.windscreen)
            {
                if (speed < 11f) continue;
                Prepare(w);
                var local = w.frame.InverseTransformPoint(at);
                int best = 0; float bestD = float.MaxValue;
                for (int i = 0; i < w.points.Length; i += 7) { float d = (w.points[i] - local).sqrMagnitude; if (d < bestD) { bestD = d; best = i; } }
                CrackHere(w, w.frame.TransformPoint(w.points[best]), 1.6f);
            }
            else if (speed > 20f && Random.value < 0.5f) Burst(w, (w.filter.transform.position - at).normalized);
        }
    }

    void Trace(Vector3 from, Vector3 dir, float reach, GlassField.Blow blow)
    {
        if (windows == null || windows.Length == 0)
        {
            if (_merged.Count > 0) BreakMerged(dir);
            return;
        }
        foreach (var w in windows)
        {
            if (w.gone || w.filter == null) continue;
            Prepare(w);
            var o = w.frame.InverseTransformPoint(from);
            var d = w.frame.InverseTransformDirection(dir);
            float t = HitTriangles(w, o, d, reach);
            if (t < 0f) continue;
            Struck(w, w.frame.TransformPoint(o + d * t), dir, blow);
        }
    }

    /// <summary>Distance along the ray to the window, or -1. The mesh is a window's few hundred triangles, and this only runs when the line crosses the car.</summary>
    static float HitTriangles(Window w, Vector3 o, Vector3 d, float reach)
    {
        float best = -1f;
        var v = w.points; var tris = w.tris;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 a = v[tris[i]], e1 = v[tris[i + 1]] - a, e2 = v[tris[i + 2]] - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) continue;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float vv = Vector3.Dot(d, q) * inv;
            if (vv < 0f || u + vv > 1f) continue;
            float t = Vector3.Dot(e2, q) * inv;
            if (t > 0f && t <= reach && (best < 0f || t < best)) best = t;
        }
        return best;
    }

    /// <summary>The window's triangles in its frame's space, read once. Merged and rig meshes are kept readable for this.</summary>
    void Prepare(Window w)
    {
        if (w.points != null) return;
        var mesh = w.filter.sharedMesh;
        if (mesh == null || !mesh.isReadable) { w.points = System.Array.Empty<Vector3>(); w.tris = System.Array.Empty<int>(); return; }
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var all = mesh.GetTriangles(w.submesh);
        int start = w.start, count = w.count > 0 ? w.count : all.Length;
        var toFrame = w.frame.worldToLocalMatrix * w.filter.transform.localToWorldMatrix;
        var map = new Dictionary<int, int>(count / 2);
        var pts = new List<Vector3>(count / 2); var ns = new List<Vector3>(count / 2);
        var tris = new int[count];
        for (int i = 0; i < count; i++)
        {
            int old = all[start + i];
            if (!map.TryGetValue(old, out int fresh))
            {
                fresh = pts.Count; map[old] = fresh;
                pts.Add(toFrame.MultiplyPoint3x4(verts[old]));
                ns.Add(normals != null && old < normals.Length ? toFrame.MultiplyVector(normals[old]).normalized : Vector3.up);
            }
            tris[i] = fresh;
        }
        w.points = pts.ToArray(); w.tris = tris;
        w.surface = GlassSurface.FromMesh(w.points, ns.ToArray(), tris, 0, tris.Length);
    }

    void Struck(Window w, Vector3 at, Vector3 dir, GlassField.Blow blow)
    {
        if (!w.windscreen || blow == GlassField.Blow.Blast) { Burst(w, dir); return; }
        int add = blow == GlassField.Blow.Heavy ? 2 : 1;
        w.hits += add;
        if (w.hits >= WindscreenLasts) { Burst(w, dir); return; }
        CrackHere(w, at, add);
    }

    void CrackHere(Window w, Vector3 at, float strength)
    {
        Prepare(w);
        if (w.surface == null) return;
        w.cracks ??= GlassFX.NewCracks(w.frame);
        GlassFracture.Crack(w.surface, w.cracks, w.frame.InverseTransformPoint(at), strength);
        GlassFX.Play(WorldSounds.GlassCrack(), at, 0.9f);
        NoiseSystem.Emit(at, 20f, NoiseKind.Violence, gameObject);
    }

    void Burst(Window w, Vector3 dir)
    {
        if (w.gone || w.filter == null) return;
        w.gone = true;
        Prepare(w);
        var filter = w.filter;
        GlassFX.Drop(w.cracks); w.cracks = null;
        var toWorld = w.frame.localToWorldMatrix;
        var sample = new GlassSurfaceSample(w.points, w.tris, 0, w.tris.Length, toWorld);
        GlassFX.CubesFrom(sample, dir);
        Vector3 mid = sample.Point();
        GlassFX.Play(WorldSounds.GlassCubes(), mid, 1f);
        NoiseSystem.Emit(mid, 40f, NoiseKind.Violence, null);

        var mesh = filter.sharedMesh;
        if (w.count <= 0 || mesh == null || !mesh.isReadable)
        {
            var r = filter.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }
        else
        {
            // One glass mesh among the car's windows: this car takes its own copy, with those triangles taken out.
            if (!_copies.TryGetValue(mesh, out var own))
            {
                own = Instantiate(mesh);
                own.name = mesh.name + " (this car)";
                _copies[mesh] = own;
                filter.sharedMesh = own;
                _copies[own] = own;
                mesh = own;
            }
            var tris = mesh.GetTriangles(w.submesh);
            for (int i = 0; i < w.count; i++) tris[w.start + i] = tris[w.start];   // a triangle with no area draws nothing, and the rest keep their places
            mesh.SetTriangles(tris, w.submesh);
        }
        // From across the street it is all one glass: gone, once any has.
        if (!_farCleared && farRenderers != null)
        {
            _farCleared = true;
            var clear = GlassFX.Clear;
            foreach (var far in farRenderers)
            {
                if (far == null) continue;
                var mats = far.sharedMaterials;
                if (farGlassSlot < mats.Length) { mats[farGlassSlot] = clear; far.sharedMaterials = mats; }
            }
        }
    }

    void BreakMerged(Vector3 dir)
    {
        if (_mergedGone || _merged.Count == 0) return;
        _mergedGone = true;
        var bounds = new Bounds(_merged[0].r.bounds.center, Vector3.zero);
        var clear = GlassFX.Clear;
        foreach (var (r, slot) in _merged)
        {
            if (r == null) continue;
            bounds.Encapsulate(r.bounds);
            var mats = r.sharedMaterials;
            mats[slot] = clear;
            r.sharedMaterials = mats;
        }
        Vector3 corner = bounds.min + Vector3.up * bounds.size.y * 0.45f;
        GlassFX.Cubes(corner, new Vector3(bounds.size.x, 0f, 0f), new Vector3(0f, bounds.size.y * 0.4f, bounds.size.z), dir, 0f);
        GlassFX.Play(WorldSounds.GlassCubes(), bounds.center, 1f);
        NoiseSystem.Emit(bounds.center, 40f, NoiseKind.Violence, null);
    }

    void OnDestroy()
    {
        foreach (var kv in _copies) if (kv.Value != null && kv.Key != kv.Value) Destroy(kv.Value);
    }
}
