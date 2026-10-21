#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town's mesh builder: one mesh per object with a submesh per material, so a house is one
    /// renderer and a handful of draw calls rather than a hundred cubes.
    ///
    /// <b>Texture coordinates are world metres</b>, projected from each face's normal (horizontal faces from
    /// above, walls along their own run), and each material's tiling is one over its tile size. Two walls built
    /// in two calls therefore meet with the plaster continuous across the join, a window cut into a wall does
    /// not stretch the wall around it, and overlapping coplanar surfaces of one material (two pieces of road
    /// meeting in a junction) shade identically and never flicker.
    ///
    /// Winding is taken from a normal the caller states, not from vertex order, so nothing built here can come
    /// out inside-out (the Fairground's first walkways did, and were invisible).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private sealed class TownMesh
        {
            private sealed class Part
            {
                public readonly List<Vector3> v = new List<Vector3>();
                public readonly List<Vector3> n = new List<Vector3>();
                public readonly List<Vector2> uv = new List<Vector2>();
                public readonly List<int> t = new List<int>();
            }

            private readonly Dictionary<Material, Part> _parts = new Dictionary<Material, Part>();
            private readonly List<Material> _order = new List<Material>();

            /// <summary>Added to every texture coordinate written after it is set: breaks repetition between buildings.</summary>
            public Vector2 UVOffset;

            public int Triangles { get { int c = 0; foreach (var p in _parts.Values) c += p.t.Count / 3; return c; } }

            /// <summary>The materials drawn so far, in order.</summary>
            public IReadOnlyList<Material> Materials => _order;

            /// <summary>Moves everything drawn with <paramref name="from"/> onto <paramref name="to"/> (merged if both are used).</summary>
            public void Swap(Material from, Material to)
            {
                if (from == to || !_parts.TryGetValue(from, out var src)) return;
                var dst = P(to);
                int s = dst.v.Count;
                dst.v.AddRange(src.v); dst.n.AddRange(src.n); dst.uv.AddRange(src.uv);
                foreach (int i in src.t) dst.t.Add(i + s);
                _parts.Remove(from); _order.Remove(from);
            }
            public bool Empty => _parts.Count == 0;

            private Part P(Material m)
            {
                if (m == null) m = _fallback ??= new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (!_parts.TryGetValue(m, out var p)) { p = new Part(); _parts[m] = p; _order.Add(m); }
                return p;
            }
            private static Material _fallback;

            private Vector2 PlanarUV(Vector3 p, Vector3 normal)
            {
                if (Mathf.Abs(normal.y) >= 0.7f) return new Vector2(p.x, p.z) + UVOffset;
                var t = Vector3.Cross(Vector3.up, normal);
                t = t.sqrMagnitude < 1e-6f ? Vector3.right : t.normalized;
                return new Vector2(Vector3.Dot(p, t), p.y) + UVOffset;
            }

            /// <summary>A flat quad facing <paramref name="normal"/>. Corners in any rim order.</summary>
            public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                var face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-10f) return;
                if (Vector3.Dot(face, normal) < 0f) { var tmp = b; b = d; d = tmp; }
                var nn = normal.normalized;
                var p = P(m);
                int s = p.v.Count;
                foreach (var q in new[] { a, b, c, d }) { p.v.Add(q); p.n.Add(nn); p.uv.Add(PlanarUV(q, nn)); }
                p.t.Add(s); p.t.Add(s + 1); p.t.Add(s + 2);
                p.t.Add(s); p.t.Add(s + 2); p.t.Add(s + 3);
            }

            /// <summary>A quad with its own texture coordinates (signs, clock faces, anything mapped once).</summary>
            public void QuadUV(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
                               Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                var face = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(face, normal) < 0f) { (b, d) = (d, b); (ub, ud) = (ud, ub); }
                var nn = normal.normalized;
                var p = P(m);
                int s = p.v.Count;
                p.v.Add(a); p.v.Add(b); p.v.Add(c); p.v.Add(d);
                for (int i = 0; i < 4; i++) p.n.Add(nn);
                p.uv.Add(ua); p.uv.Add(ub); p.uv.Add(uc); p.uv.Add(ud);
                p.t.Add(s); p.t.Add(s + 1); p.t.Add(s + 2);
                p.t.Add(s); p.t.Add(s + 2); p.t.Add(s + 3);
            }

            /// <summary>A triangle facing <paramref name="normal"/>.</summary>
            public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                var face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-10f) return;
                if (Vector3.Dot(face, normal) < 0f) { var tmp = b; b = c; c = tmp; }
                var nn = face.normalized; if (Vector3.Dot(nn, normal) < 0f) nn = -nn;
                var p = P(m);
                int s = p.v.Count;
                foreach (var q in new[] { a, b, c }) { p.v.Add(q); p.n.Add(nn); p.uv.Add(PlanarUV(q, nn)); }
                p.t.Add(s); p.t.Add(s + 1); p.t.Add(s + 2);
            }

            /// <summary>A smooth-shaded triangle strip vertex block: callers that build curved surfaces use this.</summary>
            public void Smooth(Material m, IList<Vector3> verts, IList<Vector3> normals, IList<Vector2> uvs, IList<int> tris)
            {
                var p = P(m);
                int s = p.v.Count;
                p.v.AddRange(verts); p.n.AddRange(normals);
                for (int i = 0; i < verts.Count; i++) p.uv.Add(uvs != null ? uvs[i] + UVOffset : PlanarUV(verts[i], normals[i]));
                foreach (int i in tris) p.t.Add(s + i);
            }

            /// <summary>A box, turned by <paramref name="rotation"/> about its centre. <paramref name="bottom"/> false leaves the underside open.</summary>
            public void Box(Material m, Vector3 centre, Vector3 size, Quaternion rotation, bool bottom = true, bool top = true)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
                Vector3 up = rotation * Vector3.up, fw = rotation * Vector3.forward, rt = rotation * Vector3.right;
                if (top) Quad(m, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), up);
                if (bottom) Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -up);
                Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), fw);
                Quad(m, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -fw);
                Quad(m, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), rt);
                Quad(m, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -rt);
            }

            /// <summary>An axis-aligned box from its minimum and maximum corners.</summary>
            public void BoxMinMax(Material m, Vector3 min, Vector3 max, bool bottom = true, bool top = true)
                => Box(m, (min + max) * 0.5f, max - min, Quaternion.identity, bottom, top);

            /// <summary>An upright cylinder (or a cone, if the radii differ), smooth sides, optional caps.</summary>
            public void Cylinder(Material m, Vector3 baseCentre, float radius, float height, int segments,
                                 bool capTop = true, bool capBottom = false, float topRadius = -1f)
            {
                if (topRadius < 0f) topRadius = radius;
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                float slope = (radius - topRadius) / Mathf.Max(0.001f, height);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var nrm = (dir + Vector3.up * slope).normalized;
                    float u = a * Mathf.Max(radius, topRadius);
                    verts.Add(baseCentre + dir * radius); norms.Add(nrm); uvs.Add(new Vector2(u, baseCentre.y));
                    verts.Add(baseCentre + dir * topRadius + Vector3.up * height); norms.Add(nrm); uvs.Add(new Vector2(u, baseCentre.y + height));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    tris.Add(a0); tris.Add(a1); tris.Add(b1);
                    tris.Add(a0); tris.Add(b1); tris.Add(b0);
                }
                FixWinding(verts, norms, tris);
                Smooth(m, verts, norms, uvs, tris);
                if (capTop && topRadius > 0.001f) Disc(m, baseCentre + Vector3.up * height, topRadius, segments, Vector3.up);
                if (capBottom) Disc(m, baseCentre, radius, segments, Vector3.down);
            }

            public void Disc(Material m, Vector3 centre, float radius, int segments, Vector3 normal)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                    Tri(m, centre, centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                        centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius, normal);
                }
            }

            /// <summary>
            /// A surface of revolution about the vertical through <paramref name="baseCentre"/>: profile points are
            /// (radius, height) from bottom to top. Domes, onion domes, minaret caps, finials, water tanks.
            /// </summary>
            public void Revolve(Material m, Vector3 baseCentre, IList<Vector2> profile, int segments)
            {
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                int rows = profile.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float arc = 0f;
                    for (int k = 0; k < rows; k++)
                    {
                        Vector2 prev = profile[Mathf.Max(0, k - 1)], next = profile[Mathf.Min(rows - 1, k + 1)];
                        Vector2 tan = (next - prev).normalized;
                        var nrm = (dir * tan.y - Vector3.up * tan.x).normalized;   // outward
                        if (k > 0) arc += Vector2.Distance(profile[k], profile[k - 1]);
                        verts.Add(baseCentre + dir * profile[k].x + Vector3.up * profile[k].y);
                        norms.Add(nrm);
                        uvs.Add(new Vector2(a * 3f, arc));
                    }
                }
                for (int i = 0; i < segments; i++)
                    for (int k = 0; k + 1 < rows; k++)
                    {
                        int a0 = i * rows + k, a1 = a0 + 1, b0 = a0 + rows, b1 = b0 + 1;
                        tris.Add(a0); tris.Add(a1); tris.Add(b1);
                        tris.Add(a0); tris.Add(b1); tris.Add(b0);
                    }
                FixWinding(verts, norms, tris);
                Smooth(m, verts, norms, uvs, tris);
            }

            /// <summary>Flips any triangle whose winding disagrees with its vertices' stated normals.</summary>
            private static void FixWinding(List<Vector3> v, List<Vector3> n, List<int> t)
            {
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    var avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                    if (Vector3.Dot(face, avg) < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
                }
            }

            /// <summary>
            /// A flat wall face from <paramref name="p0"/> to <paramref name="p1"/> (bottom corners), <paramref name="height"/>
            /// tall, facing <paramref name="normal"/>, with rectangular holes. Each hole is (u0, u1, v0, v1) in metres along
            /// the wall from p0 and up from its base. The face is cut into columns between hole edges and each column
            /// filled around the holes, so nothing overlaps and nothing is left out.
            /// </summary>
            public void WallWithHoles(Material m, Vector3 p0, Vector3 p1, float height, Vector3 normal, List<Vector4> holes)
            {
                Vector3 run = p1 - p0;
                float length = run.magnitude;
                if (length < 0.01f || height < 0.01f) return;
                Vector3 u = run / length;
                Vector3 W(float a, float b) => p0 + u * a + Vector3.up * b;

                if (holes == null || holes.Count == 0)
                {
                    Quad(m, W(0, 0), W(length, 0), W(length, height), W(0, height), normal);
                    return;
                }

                var cuts = new List<float> { 0f, length };
                foreach (var h in holes) { cuts.Add(Mathf.Clamp(h.x, 0f, length)); cuts.Add(Mathf.Clamp(h.y, 0f, length)); }
                cuts.Sort();
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    float a = cuts[c], b = cuts[c + 1];
                    if (b - a < 0.002f) continue;
                    float mid = (a + b) * 0.5f;
                    var spans = new List<Vector2>();
                    foreach (var h in holes) if (mid > h.x && mid < h.y) spans.Add(new Vector2(Mathf.Max(0f, h.z), Mathf.Min(height, h.w)));
                    spans.Sort((x, y) => x.x.CompareTo(y.x));
                    float y0 = 0f;
                    foreach (var s in spans)
                    {
                        if (s.x > y0 + 0.002f) Quad(m, W(a, y0), W(b, y0), W(b, s.x), W(a, s.x), normal);
                        y0 = Mathf.Max(y0, s.y);
                    }
                    if (height > y0 + 0.002f) Quad(m, W(a, y0), W(b, y0), W(b, height), W(a, height), normal);
                }
            }

            /// <summary>
            /// A wall face with openings, any of them arched: each hole is (u0, u1, v0, v1) like
            /// <see cref="WallWithHoles"/>, and <paramref name="rises"/> (by the hole's index; missing or zero = flat) is
            /// how far its head rises above v1 in a segmental arch (half the width: a round one). The face is cut into
            /// columns at the hole edges and, inside an arched hole, into thin strips, and each column is filled between
            /// the holes stacked in it, the fill above an arch following its curve.
            /// </summary>
            public void WallWithOpenings(Material m, Vector3 p0, Vector3 p1, float height, Vector3 normal, List<Vector4> holes,
                                         IDictionary<int, float> rises, int strips = 10)
            {
                if (rises == null || rises.Count == 0) { WallWithHoles(m, p0, p1, height, normal, holes); return; }
                Vector3 run = p1 - p0;
                float length = run.magnitude;
                if (length < 0.01f || height < 0.01f) return;
                Vector3 u = run / length;
                Vector3 W(float a, float b) => p0 + u * a + Vector3.up * b;
                float Rise(int i) => rises.TryGetValue(i, out float r) ? r : 0f;

                var cuts = new List<float> { 0f, length };
                for (int i = 0; i < holes.Count; i++)
                {
                    var h = holes[i];
                    cuts.Add(Mathf.Clamp(h.x, 0f, length)); cuts.Add(Mathf.Clamp(h.y, 0f, length));
                    if (Rise(i) > 0f)
                        for (int s = 1; s < strips; s++) cuts.Add(Mathf.Clamp(Mathf.Lerp(h.x, h.y, s / (float)strips), 0f, length));
                }
                cuts.Sort();
                var spans = new List<(float v0, float topA, float topB)>();
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    float a = cuts[c], b = cuts[c + 1];
                    if (b - a < 0.002f) continue;
                    float mid = (a + b) * 0.5f;
                    spans.Clear();
                    for (int i = 0; i < holes.Count; i++)
                    {
                        var h = holes[i];
                        if (mid <= h.x || mid >= h.y) continue;
                        float half = (h.y - h.x) * 0.5f, cx = (h.x + h.y) * 0.5f, r = Rise(i);
                        spans.Add((Mathf.Max(0f, h.z), Mathf.Min(height, h.w + SegmentTop(a - cx, half, r)), Mathf.Min(height, h.w + SegmentTop(b - cx, half, r))));
                    }
                    spans.Sort((x, y) => x.v0.CompareTo(y.v0));
                    float ya = 0f, yb = 0f;
                    foreach (var sp in spans)
                    {
                        if (sp.v0 > Mathf.Max(ya, yb) + 0.002f) Quad(m, W(a, ya), W(b, yb), W(b, sp.v0), W(a, sp.v0), normal);
                        ya = Mathf.Max(ya, sp.topA); yb = Mathf.Max(yb, sp.topB);
                    }
                    if (height > Mathf.Max(ya, yb) + 0.002f) Quad(m, W(a, ya), W(b, yb), W(b, height), W(a, height), normal);
                }
            }

            /// <summary>How far a segmental arch of <paramref name="rise"/> over a half-span stands above its springing at
            /// <paramref name="x"/> from its centre (zero rise: flat; rise = half-span: a semicircle).</summary>
            public static float SegmentTop(float x, float halfSpan, float rise)
            {
                if (rise <= 0.001f || halfSpan <= 0.001f) return 0f;
                float radius = (halfSpan * halfSpan + rise * rise) / (2f * rise);
                float xx = Mathf.Min(Mathf.Abs(x), halfSpan);
                return Mathf.Sqrt(Mathf.Max(0f, radius * radius - xx * xx)) - (radius - rise);
            }

            /// <summary>
            /// A wall face with arched openings (pointed when <paramref name="pointed"/>): each opening is
            /// (centre u, width, springing height). Built from thin vertical strips, each filled below the sill
            /// (none: arches go to the ground) and above the arch curve, so the curve is as smooth as the strip count.
            /// </summary>
            public void WallWithArches(Material m, Vector3 p0, Vector3 p1, float height, Vector3 normal,
                                       List<Vector3> arches, bool pointed, int strips = 14)
            {
                Vector3 run = p1 - p0;
                float length = run.magnitude;
                Vector3 u = run / Mathf.Max(0.001f, length);
                Vector3 W(float a, float b) => p0 + u * a + Vector3.up * b;
                var cuts = new List<float> { 0f, length };
                foreach (var ar in arches)
                    for (int s = 0; s <= strips; s++) cuts.Add(Mathf.Clamp(ar.x - ar.y * 0.5f + ar.y * s / strips, 0f, length));
                cuts.Sort();
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    float a = cuts[c], b = cuts[c + 1];
                    if (b - a < 0.002f) continue;
                    float topA = 0f, topB = 0f;
                    bool inside = false;
                    foreach (var ar in arches)
                    {
                        float mid = (a + b) * 0.5f;
                        if (mid <= ar.x - ar.y * 0.5f || mid >= ar.x + ar.y * 0.5f) continue;
                        inside = true;
                        topA = ArchTop(a - ar.x, ar.y * 0.5f, ar.z, pointed);
                        topB = ArchTop(b - ar.x, ar.y * 0.5f, ar.z, pointed);
                    }
                    if (!inside) { Quad(m, W(a, 0), W(b, 0), W(b, height), W(a, height), normal); continue; }
                    topA = Mathf.Min(topA, height); topB = Mathf.Min(topB, height);
                    if (height - Mathf.Max(topA, topB) > -0.001f)
                    {
                        var qa = W(a, topA); var qb = W(b, topB);
                        Quad(m, qa, qb, W(b, height), W(a, height), normal);
                    }
                }
            }

            /// <summary>The height of an arch's intrados at <paramref name="x"/> from its centre.</summary>
            public static float ArchTop(float x, float halfWidth, float spring, bool pointed)
            {
                float t = Mathf.Clamp(Mathf.Abs(x) / Mathf.Max(0.01f, halfWidth), 0f, 1f);
                if (!pointed) return spring + halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                // Two arcs of radius 1.4 half-widths whose centres sit inside the opposite jamb: a pointed (Islamic) arch.
                float r = 1.4f * halfWidth;
                float cx = r - halfWidth;                        // centre distance from the axis, on the far side
                float dx = Mathf.Abs(x) + cx;
                return spring + Mathf.Sqrt(Mathf.Max(0f, r * r - dx * dx));
            }

            /// <summary>The soffit (underside) of an arch through a wall of <paramref name="depth"/>, as strips.</summary>
            public void ArchSoffit(Material m, Vector3 p0, Vector3 u, Vector3 inward, float centreU, float width, float spring,
                                   float depth, bool pointed, int strips = 14)
            {
                for (int s = 0; s < strips; s++)
                {
                    float a = -width * 0.5f + width * s / strips, b = -width * 0.5f + width * (s + 1) / strips;
                    float ya = ArchTop(a, width * 0.5f, spring, pointed), yb = ArchTop(b, width * 0.5f, spring, pointed);
                    var pa = p0 + u * (centreU + a) + Vector3.up * ya;
                    var pb = p0 + u * (centreU + b) + Vector3.up * yb;
                    var down = Vector3.Cross(pb - pa, inward).normalized;
                    if (down.y > 0f) down = -down;
                    Quad(m, pa, pb, pb + inward * depth, pa + inward * depth, down);
                }
                // The two jambs.
                var j0 = p0 + u * (centreU - width * 0.5f); var j1 = p0 + u * (centreU + width * 0.5f);
                Quad(m, j0, j0 + Vector3.up * spring, j0 + Vector3.up * spring + inward * depth, j0 + inward * depth, u);
                Quad(m, j1, j1 + Vector3.up * spring, j1 + Vector3.up * spring + inward * depth, j1 + inward * depth, -u);
            }

            /// <summary>A straight flight of solid steps from <paramref name="foot"/> along <paramref name="dir"/>, rising <paramref name="rise"/>.</summary>
            public void Stairs(Material m, Vector3 foot, Vector3 dir, float width, float rise, float riser = 0.18f, float tread = 0.28f)
            {
                int steps = Mathf.Max(1, Mathf.RoundToInt(rise / riser));
                float r = rise / steps;
                dir.y = 0f; dir.Normalize();
                var side = Vector3.Cross(Vector3.up, dir);
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                for (int i = 0; i < steps; i++)
                {
                    float h = r * (i + 1);
                    var c = foot + dir * (tread * (i + 0.5f)) + Vector3.up * (h * 0.5f);
                    Box(m, c, new Vector3(width, h, tread), rot, bottom: false);
                }
            }

            /// <summary>
            /// Makes the GameObject: one mesh, a submesh per material, a MeshCollider on the same mesh unless told
            /// otherwise. Returns null when nothing was built.
            /// </summary>
            public GameObject Build(Transform parent, string name, int layer, string tag, bool collider = true,
                                    ShadowCastingMode shadows = ShadowCastingMode.On,
                                    UnityEditor.ModelImporterMeshCompression compression = UnityEditor.ModelImporterMeshCompression.Off,
                                    bool tangents = true)
            {
                if (_parts.Count == 0) return null;
                var mesh = new Mesh { name = name };
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
                var sub = new List<int[]>();
                foreach (var m in _order)
                {
                    var p = _parts[m];
                    int start = verts.Count;
                    verts.AddRange(p.v); norms.AddRange(p.n); uvs.AddRange(p.uv);
                    var t = new int[p.t.Count];
                    for (int i = 0; i < t.Length; i++) t[i] = p.t[i] + start;
                    sub.Add(t);
                }
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = sub.Count;
                for (int i = 0; i < sub.Count; i++) mesh.SetTriangles(sub[i], i);
                mesh.RecalculateBounds();
                // Tangents only where a material has a normal map to use them: a third of each vertex otherwise wasted.
                if (tangents) mesh.RecalculateTangents();
                // The town's meshes live in its scene: compressed there, or the scene is the better part of a gigabyte.
                if (compression != UnityEditor.ModelImporterMeshCompression.Off) UnityEditor.MeshUtility.SetMeshCompression(mesh, compression);

                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.layer = layer;
                if (!string.IsNullOrEmpty(tag)) go.tag = tag;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = _order.ToArray();
                mr.shadowCastingMode = shadows;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic |
                    UnityEditor.StaticEditorFlags.OccluderStatic | UnityEditor.StaticEditorFlags.OccludeeStatic);
                return go;
            }
        }
    }
}
#endif
