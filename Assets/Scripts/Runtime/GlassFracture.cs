using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How glass really breaks, as geometry on the pane itself (Docs/DESIGN_NOTES.md "Glass that breaks like glass"). A blow makes a crushed bull's-eye and
/// cracks running out from it; a crack ends at the frame or at a crack that is already there, which is why a second round's web stops at the first's.
/// A picture on a quad cannot do this: the old one faced the shot, hung off the windscreen over the bonnet, and was the same picture every time.
/// </summary>
public sealed class GlassSurface
{
    readonly Vector3 _origin, _u, _v, _normal;
    readonly Vector2[] _flat;
    readonly Vector3[] _points, _normals;
    readonly int[] _tris;
    readonly List<int>[] _grid;
    readonly Vector2 _min;
    readonly Vector2 _cellSize;
    const int Cells = 10;

    /// <summary>The cracks so far, in the surface's flat space, two points each: new cracks end on them.</summary>
    public readonly List<Vector2> Segments = new List<Vector2>();
    public float Size { get; }
    /// <summary>Where the first blow landed: a pane that goes on to shatter breaks from there.</summary>
    public Vector2 FirstHit;
    public bool Hit;

    GlassSurface(Vector3[] points, Vector3[] normals, int[] tris)
    {
        _points = points; _normals = normals; _tris = tris;
        // The plane the pane mostly lies in: the area-weighted normal, through the middle.
        Vector3 area = Vector3.zero, centre = Vector3.zero;
        for (int i = 0; i < tris.Length; i += 3)
            area += Vector3.Cross(points[tris[i + 1]] - points[tris[i]], points[tris[i + 2]] - points[tris[i]]);
        foreach (var p in points) centre += p;
        _origin = points.Length > 0 ? centre / points.Length : Vector3.zero;
        _normal = area.sqrMagnitude > 1e-10f ? area.normalized : Vector3.forward;
        _u = Vector3.Cross(_normal, Mathf.Abs(_normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        _v = Vector3.Cross(_normal, _u);

        _flat = new Vector2[points.Length];
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
        for (int i = 0; i < points.Length; i++)
        {
            var f = To2D(points[i]); _flat[i] = f;
            min = Vector2.Min(min, f); max = Vector2.Max(max, f);
        }
        _min = min;
        var extent = max - min;
        Size = Mathf.Max(0.05f, Mathf.Min(extent.x, extent.y));
        _cellSize = new Vector2(Mathf.Max(1e-3f, extent.x) / Cells, Mathf.Max(1e-3f, extent.y) / Cells);
        // A coarse grid: a crack asks where it is a few hundred times, and a curved window is about a thousand triangles.
        _grid = new List<int>[Cells * Cells];
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector2 a = _flat[tris[t]], b = _flat[tris[t + 1]], c = _flat[tris[t + 2]];
            Vector2 lo = Vector2.Min(a, Vector2.Min(b, c)), hi = Vector2.Max(a, Vector2.Max(b, c));
            int x0 = CellX(lo.x), x1 = CellX(hi.x), y0 = CellY(lo.y), y1 = CellY(hi.y);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    (_grid[y * Cells + x] ??= new List<int>(8)).Add(t);
        }
    }

    int CellX(float v) => Mathf.Clamp(Mathf.FloorToInt((v - _min.x) / _cellSize.x), 0, Cells - 1);
    int CellY(float v) => Mathf.Clamp(Mathf.FloorToInt((v - _min.y) / _cellSize.y), 0, Cells - 1);

    public Vector2 To2D(Vector3 p) { var d = p - _origin; return new Vector2(Vector3.Dot(d, _u), Vector3.Dot(d, _v)); }

    /// <summary>The point of the glass at a flat position, with its normal; false off the glass (past the frame).</summary>
    public bool Locate(Vector2 q, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        var list = _grid[CellY(q.y) * Cells + CellX(q.x)];
        if (list == null) return false;
        foreach (int t in list)
        {
            int ia = _tris[t], ib = _tris[t + 1], ic = _tris[t + 2];
            Vector2 a = _flat[ia], v0 = _flat[ib] - a, v1 = _flat[ic] - a, v2 = q - a;
            float d = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(d) < 1e-9f) continue;
            float s = (v2.x * v1.y - v1.x * v2.y) / d, w = (v0.x * v2.y - v2.x * v0.y) / d;
            if (s < -1e-4f || w < -1e-4f || s + w > 1.0001f) continue;
            float r = 1f - s - w;
            point = _points[ia] * r + _points[ib] * s + _points[ic] * w;
            normal = (_normals[ia] * r + _normals[ib] * s + _normals[ic] * w).normalized;
            return true;
        }
        return false;
    }

    public static GlassSurface FromMesh(Vector3[] vertices, Vector3[] normals, int[] tris, int start, int count)
    {
        var map = new Dictionary<int, int>(count);
        var pts = new List<Vector3>(count / 2); var ns = new List<Vector3>(count / 2);
        var out_ = new int[count];
        for (int i = 0; i < count; i++)
        {
            int old = tris[start + i];
            if (!map.TryGetValue(old, out int fresh))
            {
                fresh = pts.Count; map[old] = fresh; pts.Add(vertices[old]);
                ns.Add(normals != null && old < normals.Length ? normals[old] : Vector3.up);
            }
            out_[i] = fresh;
        }
        return new GlassSurface(pts.ToArray(), ns.ToArray(), out_);
    }

    public static GlassSurface FromRect(Vector3 corner, Vector3 u, Vector3 v, Vector3 normal)
    {
        var pts = new[] { corner, corner + u, corner + u + v, corner + v };
        return new GlassSurface(pts, new[] { normal, normal, normal, normal }, new[] { 0, 1, 2, 0, 2, 3 });
    }
}

/// <summary>The lines of one piece of glass: one mesh, one draw, added to with each blow. Vertex colours carry how brightly each line catches the light.</summary>
public sealed class CrackMesh
{
    public readonly GameObject Object;
    readonly Mesh _mesh;
    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<Color32> _colours = new List<Color32>();
    readonly List<int> _tris = new List<int>();

    public CrackMesh(Transform parent, Material material)
    {
        Object = new GameObject("Cracks");
        Object.transform.SetParent(parent, false);
        Object.layer = parent.gameObject.layer;
        _mesh = new Mesh { name = "Cracks" };
        _mesh.MarkDynamic();
        Object.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var r = Object.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    /// <summary>A ribbon along the glass from a to b, lying in its surface.</summary>
    public void Line(Vector3 a, Vector3 b, Vector3 normal, float widthA, float widthB, Color32 colour)
    {
        Vector3 along = b - a;
        if (along.sqrMagnitude < 1e-8f) return;
        Vector3 side = Vector3.Cross(along, normal).normalized;
        Vector3 lift = normal * 0.0015f;
        int s = _verts.Count;
        _verts.Add(a + lift - side * widthA * 0.5f); _verts.Add(a + lift + side * widthA * 0.5f);
        _verts.Add(b + lift + side * widthB * 0.5f); _verts.Add(b + lift - side * widthB * 0.5f);
        for (int i = 0; i < 4; i++) _colours.Add(colour);
        _tris.Add(s); _tris.Add(s + 2); _tris.Add(s + 1); _tris.Add(s); _tris.Add(s + 3); _tris.Add(s + 2);
    }

    /// <summary>A small filled shape: the crushed glass and the hole of a bull's-eye.</summary>
    public void Fan(Vector3 centre, IList<Vector3> rim, Vector3 normal, Color32 middle, Color32 edge)
    {
        Vector3 lift = normal * 0.002f;
        int s = _verts.Count;
        _verts.Add(centre + lift); _colours.Add(middle);
        for (int i = 0; i < rim.Count; i++) { _verts.Add(rim[i] + lift); _colours.Add(edge); }
        for (int i = 0; i < rim.Count; i++)
        {
            _tris.Add(s); _tris.Add(s + 1 + (i + 1) % rim.Count); _tris.Add(s + 1 + i);
        }
    }

    public void Commit()
    {
        _mesh.Clear(false);
        _mesh.indexFormat = _verts.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(_verts);
        _mesh.SetColors(_colours);
        _mesh.SetTriangles(_tris, 0, false);
        _mesh.RecalculateBounds();
    }
}

public static class GlassFracture
{
    static readonly List<List<Vector2>> _radials = new List<List<Vector2>>(16);
    static readonly List<Vector3> _rim = new List<Vector3>(10);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _radials.Clear(); _rim.Clear(); }

    static Color32 Lit(float amount)
    {
        byte v = (byte)Mathf.Clamp(170f + amount * 85f, 0f, 255f);
        return new Color32(v, v, (byte)Mathf.Min(255, v + 4), (byte)Mathf.Clamp(150f + amount * 105f, 0f, 255f));
    }

    /// <summary>
    /// A blow at <paramref name="at"/> (in the surface's own space). <paramref name="strength"/>: 1 a rifle round, 2 a kick or a heavy round. The hole and the crushed ring,
    /// radial cracks to the frame or the nearest crack, some branching, and a few concentric arcs between them: the spider web of real glass.
    /// </summary>
    public static void Crack(GlassSurface glass, CrackMesh mesh, Vector3 at, float strength)
    {
        var centre = glass.To2D(at);
        if (!glass.Hit) { glass.Hit = true; glass.FirstHit = centre; }
        float scale = Mathf.Clamp(glass.Size, 0.35f, 1.1f);
        int spokes = Mathf.RoundToInt(Random.Range(8f, 13f) * Mathf.Lerp(1f, 1.3f, strength - 1f));
        float reach = scale * Random.Range(0.42f, 0.8f) * Mathf.Sqrt(strength);

        BullsEye(glass, mesh, centre, strength);

        _radials.Clear();
        float turn = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < spokes; i++)
        {
            float a = turn + (i + Random.Range(-0.32f, 0.32f)) * Mathf.PI * 2f / spokes;
            float length = reach * Random.Range(0.45f, 1.25f);
            var path = new List<Vector2>(24);
            Run(glass, mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.012f, a, length, path, Random.Range(-0.3f, 0.5f), 0.0034f, 0);
            _radials.Add(path);
        }
        // Rings: short arcs across from one spoke to its neighbour, nearer the blow more often. They end on what they meet, like the rest.
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = reach * (0.16f + ring * 0.21f) * Random.Range(0.85f, 1.15f);
            for (int i = 0; i < _radials.Count; i++)
            {
                if (Random.value > 0.72f - ring * 0.14f) continue;
                if (!PointAt(_radials[i], centre, radius, out var a) || !PointAt(_radials[(i + 1) % _radials.Count], centre, radius * Random.Range(0.9f, 1.1f), out var b)) continue;
                Vector2 mid = Vector2.Lerp(a, b, 0.5f); mid = centre + (mid - centre) * Random.Range(0.96f, 1.03f);
                Step(glass, mesh, a, mid, 0.0024f, 0.002f, Random.Range(0.1f, 0.8f));
                Step(glass, mesh, mid, b, 0.002f, 0.0024f, Random.Range(0.1f, 0.8f));
            }
        }
        mesh.Commit();
    }

    /// <summary>The crushed cone a round leaves: a dark hole, a white ring of powdered glass and dozens of short cracks.</summary>
    static void BullsEye(GlassSurface glass, CrackMesh mesh, Vector2 centre, float strength)
    {
        if (!glass.Locate(centre, out var mid, out var n)) return;
        float crushed = Random.Range(0.016f, 0.026f) * strength;
        Ring(glass, centre, crushed, 12, 0.3f);
        if (_rim.Count > 3) mesh.Fan(mid, _rim, n, new Color32(245, 245, 245, 235), new Color32(235, 238, 238, 24));
        Ring(glass, centre, crushed * 0.34f, 8, 0.25f);
        if (_rim.Count > 3) mesh.Fan(mid, _rim, n, new Color32(8, 8, 9, 245), new Color32(16, 16, 18, 210));
        int spikes = Mathf.RoundToInt(22f * strength);
        for (int i = 0; i < spikes; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 p0 = centre + dir * crushed * 0.35f, p1 = centre + dir * crushed * Random.Range(0.9f, 2.4f);
            if (!glass.Locate(p0, out var w0, out _) || !glass.Locate(p1, out var w1, out _)) continue;
            mesh.Line(w0, w1, n, 0.0022f, 0.0006f, Lit(Random.value));
        }
    }

    static void Ring(GlassSurface glass, Vector2 centre, float radius, int sides, float jitter)
    {
        _rim.Clear();
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            float r = radius * (1f + Random.Range(-jitter, jitter));
            if (glass.Locate(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, out var p, out _)) _rim.Add(p);
        }
    }

    /// <summary>One crack walking across the glass until it has run its length, left the glass or met another crack. Thins as it goes.</summary>
    static void Run(GlassSurface glass, CrackMesh mesh, Vector2 from, float angle, float length, List<Vector2> path, float light, float width, int depth)
    {
        float done = 0f; Vector2 p = from;
        path?.Add(p);
        float bend = Random.Range(-0.05f, 0.05f);
        int guard = 0;
        while (done < length && guard++ < 64)
        {
            float step = Mathf.Min(Random.Range(0.02f, 0.05f) + done * 0.04f, length - done + 0.005f);
            angle += bend + Random.Range(-0.15f, 0.15f);
            Vector2 q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step;
            float w0 = width * (1f - done / length * 0.72f), w1 = width * (1f - (done + step) / length * 0.72f);
            bool stopped = Blocked(glass, p, ref q);
            bool off = !glass.Locate(q, out _, out _);
            if (off)
            {
                // Up to the frame itself, not a step short of it.
                Vector2 lo = p, hi = q;
                for (int i = 0; i < 5; i++) { var m = (lo + hi) * 0.5f; if (glass.Locate(m, out _, out _)) lo = m; else hi = m; }
                q = lo; stopped = true;
            }
            light = Mathf.Clamp(light + Random.Range(-0.25f, 0.25f), -0.6f, 1f);
            Step(glass, mesh, p, q, w0, w1, light);
            path?.Add(q);
            p = q; done += step;
            if (stopped) return;
            if (depth < 1 && done > 0.04f && Random.value < 0.11f)
                Run(glass, mesh, p, angle + (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.35f, 0.8f), (length - done) * Random.Range(0.25f, 0.6f), null, light, w1 * 0.8f, depth + 1);
        }
    }

    static void Step(GlassSurface glass, CrackMesh mesh, Vector2 a, Vector2 b, float w0, float w1, float light)
    {
        if (!glass.Locate(a, out var pa, out var na) || !glass.Locate(b, out var pb, out var nb)) return;
        glass.Segments.Add(a); glass.Segments.Add(b);
        mesh.Line(pa, pb, (na + nb).normalized, w0, w1, Lit(light));
    }

    /// <summary>Ends the step on the first crack it crosses. Its own last point is left out, or every crack would stop on itself.</summary>
    static bool Blocked(GlassSurface glass, Vector2 a, ref Vector2 b)
    {
        var s = glass.Segments;
        float nearest = 1f; bool hit = false;
        for (int i = 0; i + 1 < s.Count; i += 2)
        {
            Vector2 c = s[i], d = s[i + 1];
            if ((c - a).sqrMagnitude < 1e-7f || (d - a).sqrMagnitude < 1e-7f) continue;
            Vector2 r = b - a, q = d - c;
            float den = r.x * q.y - r.y * q.x;
            if (Mathf.Abs(den) < 1e-9f) continue;
            float t = ((c.x - a.x) * q.y - (c.y - a.y) * q.x) / den, u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            if (t > 0.001f && t < nearest && u >= 0f && u <= 1f) { nearest = t; hit = true; }
        }
        if (hit) b = a + (b - a) * nearest;
        return hit;
    }

    static bool PointAt(List<Vector2> path, Vector2 centre, float radius, out Vector2 point)
    {
        point = default;
        for (int i = 1; i < path.Count; i++)
        {
            float d0 = (path[i - 1] - centre).magnitude, d1 = (path[i] - centre).magnitude;
            if (d0 <= radius && d1 >= radius) { point = Vector2.Lerp(path[i - 1], path[i], Mathf.InverseLerp(d0, d1, radius)); return true; }
        }
        return false;
    }

    // ---- breaking up ---------------------------------------------------------

    public struct Cell
    {
        public Vector2[] polygon;   // metres on the pane, from its corner along u and v
        public Vector2 centre;
        public float fromBlow;      // metres from the blow
        public bool touchesFrame;
    }

    /// <summary>
    /// The pieces ordinary glass breaks into: long wedges round the blow, cut across by rings, clipped to the frame. The same pattern as its cracks, so a pane breaks
    /// along the lines it had. Each corner is shared with its neighbours, so the pieces fit together.
    /// </summary>
    public static List<Cell> Pieces(float width, float height, Vector2 blow, int maxPieces)
    {
        int spokes = Mathf.Clamp(Mathf.RoundToInt(Random.Range(9f, 13f)), 7, 16);
        float far = Mathf.Sqrt(Mathf.Max(blow.x, width - blow.x) * Mathf.Max(blow.x, width - blow.x) + Mathf.Max(blow.y, height - blow.y) * Mathf.Max(blow.y, height - blow.y)) * 1.1f;
        var radii = new List<float> { 0.015f };
        float r = Mathf.Max(0.06f, Mathf.Min(width, height) * 0.12f);
        while (r < far) { radii.Add(r); r *= Random.Range(1.55f, 2.1f); }
        radii.Add(far * 1.1f);

        // The shared grid of corners, turned and pushed about a little so nothing is regular.
        var corners = new Vector2[spokes, radii.Count];
        var angles = new float[spokes];
        float turn = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < spokes; i++) angles[i] = turn + (i + Random.Range(-0.32f, 0.32f)) * Mathf.PI * 2f / spokes;
        for (int i = 0; i < spokes; i++)
            for (int j = 0; j < radii.Count; j++)
            {
                float a = angles[i] + Random.Range(-0.05f, 0.05f);
                float rr = radii[j] * Random.Range(0.86f, 1.14f);
                corners[i, j] = blow + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            }

        var cells = new List<Cell>(spokes * radii.Count);
        for (int i = 0; i < spokes; i++)
            for (int j = 0; j + 1 < radii.Count; j++)
            {
                int n = (i + 1) % spokes;
                var clipped = Clip(new[] { corners[i, j], corners[n, j], corners[n, j + 1], corners[i, j + 1] }, width, height, out bool frame);
                if (clipped == null) continue;
                Vector2 mid = Vector2.zero; foreach (var p in clipped) mid += p; mid /= clipped.Length;
                cells.Add(new Cell { polygon = clipped, centre = mid, fromBlow = (mid - blow).magnitude, touchesFrame = frame });
                if (cells.Count >= maxPieces) return cells;
            }
        return cells;
    }

    /// <summary>Sutherland-Hodgman against the pane's rectangle.</summary>
    static Vector2[] Clip(Vector2[] poly, float w, float h, out bool frame)
    {
        frame = false;
        var input = new List<Vector2>(poly);
        var output = new List<Vector2>(8);
        for (int edge = 0; edge < 4; edge++)
        {
            output.Clear();
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 a = input[i], b = input[(i + 1) % input.Count];
                float da = Side(a, edge, w, h), db = Side(b, edge, w, h);
                if (da >= 0f) output.Add(a);
                if ((da >= 0f) != (db >= 0f)) { output.Add(a + (b - a) * (da / (da - db))); frame = true; }
            }
            if (output.Count < 3) return null;
            var swap = input; input = output; output = swap;
        }
        return input.ToArray();
    }

    static float Side(Vector2 p, int edge, float w, float h) => edge switch { 0 => p.x, 1 => w - p.x, 2 => p.y, _ => h - p.y };
}
