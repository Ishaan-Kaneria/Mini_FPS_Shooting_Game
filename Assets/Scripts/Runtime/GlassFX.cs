using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What breaking glass looks and sounds like, for the town's panes (<see cref="GlassField"/>) and the cars' (<see cref="CarGlass"/>). All
/// pooled and made on first use: cube particles for safety glass, a few meshes of falling shards, cracks and the pieces left in a frame, reused oldest
/// first, so a street of broken windows costs a fixed amount. Materials from Resources/Glass (FPSKitTownGlass.MakeMaterials).
/// </summary>
public static class GlassFX
{
    const int Systems = 4, MaxRemains = 64, MaxCracks = 40, Flights = 6;

    static Material _shard, _pane, _line;
    static Mesh _cube;
    static readonly ParticleSystem[] _systems = new ParticleSystem[Systems];
    static int _next;
    static readonly List<MeshFilter> _remains = new List<MeshFilter>();
    static int _nextRemain;
    static readonly List<CrackMesh> _cracks = new List<CrackMesh>();
    static readonly List<ShardFlight> _flights = new List<ShardFlight>();
    static int _nextFlight;
    static Transform _root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _shard = _pane = _line = null; _cube = null;
        System.Array.Clear(_systems, 0, Systems);
        _remains.Clear(); _cracks.Clear(); _flights.Clear(); _next = _nextRemain = _nextFlight = 0; _root = null;
    }

    static Material Mat(ref Material m, string name) => m != null ? m : m = Resources.Load<Material>("Glass/" + name);
    public static Material Pane => Mat(ref _pane, "Pane");
    public static Material Clear => Resources.Load<Material>("Glass/Clear");
    public static Material Shard => Mat(ref _shard, "Shard");
    public static Material CrackLine => Mat(ref _line, "CrackLine");

    public static Transform Root
    {
        get
        {
            if (_root == null)
            {
                _root = new GameObject("GlassFX").transform;
                _remains.Clear(); _cracks.Clear(); _flights.Clear(); System.Array.Clear(_systems, 0, Systems);
            }
            return _root;
        }
    }

    /// <summary>A crushed cube of safety glass, a little out of true, flat-shaded so each face flashes on its own.</summary>
    static Mesh Cube
    {
        get
        {
            if (_cube != null) return _cube;
            _cube = new Mesh { name = "GlassCube" };
            var c = new[]
            {
                new Vector3(-.5f, -.42f, -.46f), new Vector3(.46f, -.5f, -.5f), new Vector3(.5f, .44f, -.42f), new Vector3(-.44f, .5f, -.5f),
                new Vector3(-.46f, -.5f, .5f), new Vector3(.5f, -.44f, .46f), new Vector3(.42f, .5f, .5f), new Vector3(-.5f, .46f, .44f),
            };
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } };
            var vs = new List<Vector3>(24); var ns = new List<Vector3>(24); var ts = new List<int>(36);
            foreach (var f in faces)
            {
                int s = vs.Count;
                var n = Vector3.Cross(c[f[1]] - c[f[0]], c[f[2]] - c[f[0]]).normalized;
                foreach (int i in f) { vs.Add(c[i]); ns.Add(n); }
                ts.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            _cube.SetVertices(vs); _cube.SetNormals(ns); _cube.SetTriangles(ts, 0);
            return _cube;
        }
    }

    static ParticleSystem System_()
    {
        var root = Root;
        int i = _next; _next = (_next + 1) % Systems;
        if (_systems[i] != null) return _systems[i];
        var go = new GameObject("Cubes");
        go.transform.SetParent(root, false);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400; main.gravityModifier = 1.1f; main.startLifetime = 3.2f; main.startSpeed = 0f;
        main.startRotation3D = true;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.collision;
        col.enabled = true; col.type = ParticleSystemCollisionType.World; col.quality = ParticleSystemCollisionQuality.Low;
        col.bounce = 0.28f; col.dampen = 0.45f; col.lifetimeLoss = 0.03f; col.radiusScale = 0.3f;
        col.collidesWith = LayerMask.GetMask("Environment", "Default", "Vehicle");
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-12f, 12f); rot.y = new ParticleSystem.MinMaxCurve(-12f, 12f); rot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = Cube;
        r.sharedMaterial = Shard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        go.SetActive(true);
        ps.Play();
        return _systems[i] = ps;
    }

    static void EmitCube(ParticleSystem ps, ref ParticleSystem.EmitParams e, Vector3 at, Vector3 push)
    {
        e.position = at;
        e.velocity = push * Random.Range(0.1f, 1.6f) + new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(-0.5f, 0.3f), Random.Range(-0.45f, 0.45f));
        e.startSize = Random.value < 0.15f ? Random.Range(0.016f, 0.028f) : Random.Range(0.006f, 0.015f);
        e.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
        e.startColor = new Color32(255, 255, 255, 255);
        e.startLifetime = Random.Range(1.4f, 3.2f);
        ps.Emit(e, 1);
    }

    /// <summary>Safety glass letting go: small cubes from every part of a flat rectangle, falling more than flying.</summary>
    public static void Cubes(Vector3 corner, Vector3 u, Vector3 v, Vector3 push, float near)
    {
        float area = u.magnitude * v.magnitude;
        int n = Mathf.Clamp(Mathf.RoundToInt(area * 240f), 40, 300);
        if (QualityTiers.Current == GameSettings.Quality.Low) n /= 2;
        if (near > 40f) n /= 3;
        var ps = System_();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < n; i++)
            EmitCube(ps, ref e, corner + u * Random.value + v * Random.value, push);
    }

    /// <summary>The same from a real window's triangles (a car's curved glass), so the falling cloud has the window's shape, not its box's.</summary>
    public static void CubesFrom(GlassSurfaceSample sample, Vector3 push)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(sample.Area * 240f), 40, 300);
        if (QualityTiers.Current == GameSettings.Quality.Low) n /= 2;
        var ps = System_();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < n; i++) EmitCube(ps, ref e, sample.Point(), push);
    }

    /// <summary>Lines for one piece of glass. Oldest out when there are too many: a level can break a lot of windows.</summary>
    public static CrackMesh NewCracks(Transform parent)
    {
        if (_cracks.Count >= MaxCracks)
        {
            var old = _cracks[0]; _cracks.RemoveAt(0);
            if (old != null && old.Object != null) Object.Destroy(old.Object);
        }
        var mesh = new CrackMesh(parent != null ? parent : Root, CrackLine);
        _cracks.Add(mesh);
        return mesh;
    }

    public static void Drop(CrackMesh mesh)
    {
        if (mesh == null) return;
        _cracks.Remove(mesh);
        if (mesh.Object != null) Object.Destroy(mesh.Object);
    }

    /// <summary>
    /// Ordinary glass breaking: the pieces of <see cref="GlassFracture.Pieces"/>. Those at the frame may stay where they are, as real ones do, more at the sill than the head;
    /// the rest let go from the blow outwards and fall, tumbling, and lie where they land. <paramref name="blow"/> in metres on the pane.
    /// </summary>
    public static void BreakPane(Vector3 corner, Vector3 u, Vector3 v, Vector3 normal, Vector2 blow, Vector3 push, float near)
    {
        float w = u.magnitude, h = v.magnitude;
        int budget = near > 40f ? 14 : QualityTiers.Current == GameSettings.Quality.Low ? 26 : 60;
        var cells = GlassFracture.Pieces(w, h, blow, budget);
        var ux = u / Mathf.Max(1e-3f, w); var vy = v / Mathf.Max(1e-3f, h);

        var stay = new List<GlassFracture.Cell>();
        var fall = new List<GlassFracture.Cell>();
        foreach (var c in cells)
        {
            float height = c.centre.y / Mathf.Max(0.01f, h);
            bool keep = c.touchesFrame && Random.value < Mathf.Lerp(0.72f, 0.4f, height) && c.fromBlow > 0.14f;
            (keep ? stay : fall).Add(c);
        }
        Remains(corner, ux, vy, normal, stay);

        float floor = corner.y - 3f;
        Vector3 under = corner + (u + v) * 0.5f;
        if (Physics.Raycast(under + normal * 0.25f, Vector3.down, out var ground, 30f, LayerMask.GetMask("Environment", "Default"), QueryTriggerInteraction.Ignore))
            floor = ground.point.y;
        NextFlight().Launch(fall, corner, ux, vy, normal, blow, push, floor);
        // The dust of it: a little that a piece would not show.
        var ps = System_();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        Vector3 at = corner + ux * blow.x + vy * blow.y;
        for (int i = 0; i < 16; i++)
            EmitCube(ps, ref e, at + (ux * Random.Range(-1f, 1f) + vy * Random.Range(-1f, 1f)) * 0.08f, push + normal * 0.2f);
    }

    static ShardFlight NextFlight()
    {
        var root = Root;
        if (_flights.Count < Flights)
        {
            var go = new GameObject("ShardFlight");
            go.transform.SetParent(root, false);
            var f = go.AddComponent<ShardFlight>();
            _flights.Add(f);
            return f;
        }
        var it = _flights[_nextFlight]; _nextFlight = (_nextFlight + 1) % Flights;
        return it;
    }

    /// <summary>The pieces that stay in a broken frame: the real shapes the pane broke into.</summary>
    static void Remains(Vector3 corner, Vector3 ux, Vector3 vy, Vector3 normal, List<GlassFracture.Cell> stay)
    {
        var root = Root;
        MeshFilter mf;
        if (_remains.Count < MaxRemains)
        {
            var go = new GameObject("BrokenPane");
            go.transform.SetParent(root, false);
            mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = "BrokenPane" };
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Pane;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            _remains.Add(mf);
        }
        else { mf = _remains[_nextRemain]; _nextRemain = (_nextRemain + 1) % MaxRemains; }

        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>();
        Vector3 at = corner + normal * 0.006f;
        foreach (var c in stay)
        {
            int s = verts.Count;
            foreach (var p in c.polygon)
            {
                verts.Add(at + ux * p.x + vy * p.y);
                uvs.Add(new Vector2(0.5f, 0.6f));       // the clean middle of the pane: its edge is the thick dust
                norms.Add(normal);
            }
            for (int i = 1; i + 1 < c.polygon.Length; i++) { tris.Add(s); tris.Add(s + i); tris.Add(s + i + 1); tris.Add(s); tris.Add(s + i + 1); tris.Add(s + i); }
        }
        var mesh = mf.sharedMesh;
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mf.gameObject.SetActive(true);
    }

    public static void Play(AudioClip clip, Vector3 at, float volume)
    {
        if (clip != null) AudioSource.PlayClipAtPoint(clip, at, volume);
    }
}

/// <summary>Points scattered over a window's real triangles, by area, in the world.</summary>
public sealed class GlassSurfaceSample
{
    readonly Vector3[] _a, _b, _c;
    readonly float[] _sum;
    public float Area { get; }

    public GlassSurfaceSample(Vector3[] vertices, int[] tris, int start, int count, Matrix4x4 toWorld)
    {
        int n = count / 3;
        _a = new Vector3[n]; _b = new Vector3[n]; _c = new Vector3[n]; _sum = new float[n];
        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            _a[i] = toWorld.MultiplyPoint3x4(vertices[tris[start + i * 3]]);
            _b[i] = toWorld.MultiplyPoint3x4(vertices[tris[start + i * 3 + 1]]);
            _c[i] = toWorld.MultiplyPoint3x4(vertices[tris[start + i * 3 + 2]]);
            total += Vector3.Cross(_b[i] - _a[i], _c[i] - _a[i]).magnitude * 0.5f;
            _sum[i] = total;
        }
        Area = total;
    }

    public Vector3 Point()
    {
        if (_sum.Length == 0) return Vector3.zero;
        int i = System.Array.BinarySearch(_sum, Random.value * Area);
        if (i < 0) i = ~i;
        i = Mathf.Clamp(i, 0, _sum.Length - 1);
        float u = Random.value, v = Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return _a[i] + (_b[i] - _a[i]) * u + (_c[i] - _a[i]) * v;
    }
}

/// <summary>
/// One pane's falling pieces, one mesh moved by hand: no rigidbodies, no colliders, a ground height found once. They let go from the blow outwards, tumble, land, and lie
/// there until the flight is reused. A few dozen polygons a frame while it lasts; nothing at all after.
/// </summary>
public sealed class ShardFlight : MonoBehaviour
{
    class Piece
    {
        public Vector3[] shape;     // about its own middle, in the world's scale
        public Vector3 at, velocity, axis;
        public Quaternion turn;
        public float spin, delay, floor;
        public bool landed;
    }

    readonly List<Piece> _pieces = new List<Piece>();
    Mesh _mesh;
    Vector3[] _verts, _normals;
    float _ends;
    bool _rang;
    Vector3 _where;
    Vector3 _baseNormal;

    public void Launch(List<GlassFracture.Cell> cells, Vector3 corner, Vector3 ux, Vector3 vy, Vector3 normal, Vector2 blow, Vector3 push, float floor)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "Shards" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = GlassFX.Shard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }
        _pieces.Clear();
        _rang = false;
        _baseNormal = normal;
        int vertices = 0;
        var tris = new List<int>();
        var pass = Vector3.Dot(push, normal) >= 0f ? normal : -normal;     // the side the round went out of
        foreach (var c in cells)
        {
            var mid = corner + ux * c.centre.x + vy * c.centre.y;
            var shape = new Vector3[c.polygon.Length];
            for (int i = 0; i < shape.Length; i++) shape[i] = ux * (c.polygon[i].x - c.centre.x) + vy * (c.polygon[i].y - c.centre.y);
            float near = Mathf.Clamp01(1f - c.fromBlow / 1.2f);
            var p = new Piece
            {
                shape = shape, at = mid, turn = Quaternion.identity, floor = floor + Random.Range(0.005f, 0.02f),
                axis = Random.onUnitSphere,
                spin = Random.Range(40f, 240f) * (0.4f + near),
                delay = c.fromBlow * Random.Range(0.05f, 0.12f),
                // Near the blow they are driven out with the round; further off they just drop, and those high up lean out as they go.
                velocity = pass * (Random.Range(0.1f, 0.5f) + near * near * Random.Range(0.8f, 2.8f)) + push.normalized * near * 0.4f + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f)),
            };
            _pieces.Add(p);
            for (int i = 1; i + 1 < shape.Length; i++)
            {
                tris.Add(vertices); tris.Add(vertices + i); tris.Add(vertices + i + 1);
                tris.Add(vertices); tris.Add(vertices + i + 1); tris.Add(vertices + i);
            }
            vertices += shape.Length;
        }
        _where = corner + (ux + vy) * 0.5f;
        _verts = new Vector3[vertices];
        _normals = new Vector3[vertices];
        _mesh.Clear();
        Write();
        _mesh.SetTriangles(tris, 0, false);
        _mesh.bounds = new Bounds(_where, new Vector3(30f, 30f, 30f));
        _ends = Time.time + 12f;
        enabled = true;
        gameObject.SetActive(_pieces.Count > 0);
    }

    void Write()
    {
        int v = 0;
        foreach (var p in _pieces)
        {
            var n = p.turn * _baseNormal;
            foreach (var s in p.shape) { _verts[v] = p.at + p.turn * s; _normals[v] = n; v++; }
        }
        _mesh.vertices = _verts;
        _mesh.normals = _normals;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        bool moving = false;
        foreach (var p in _pieces)
        {
            if (p.landed) continue;
            moving = true;
            if (p.delay > 0f) { p.delay -= dt; continue; }
            p.velocity += Physics.gravity * dt;
            p.at += p.velocity * dt;
            p.turn = Quaternion.AngleAxis(p.spin * dt, p.axis) * p.turn;
            if (p.at.y <= p.floor)
            {
                p.at.y = p.floor;
                p.landed = true;
                // Glass lies flat where it lands.
                p.turn = Quaternion.FromToRotation(p.turn * _baseNormal, Vector3.up) * p.turn;
                if (!_rang) { _rang = true; GlassFX.Play(WorldSounds.GlassTinkle(), p.at, 0.75f); }
            }
        }
        if (moving) Write();
        else enabled = false;
        if (Time.time > _ends) gameObject.SetActive(false);
    }
}
