using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// What breaking glass looks and sounds like, for the town's panes (<see cref="GlassField"/>) and the cars' (<see cref="CarGlass"/>). All
/// pooled and made on first use: one shard mesh, a few particle systems, a few dozen crack decals and jagged remains that are reused oldest
/// first, so a street of broken windows costs a fixed amount. Materials from Resources/Glass (FPSKitTownGlass.MakeMaterials).
/// </summary>
public static class GlassFX
{
    const int Systems = 4, Decals = 48, MaxRemains = 64;

    static Material _shard, _crack, _web, _pane;
    static Mesh _shardMesh, _quad;
    static readonly ParticleSystem[] _systems = new ParticleSystem[Systems];
    static int _next;
    static readonly List<GameObject> _decals = new List<GameObject>();
    static int _nextDecal;
    static readonly List<MeshFilter> _remains = new List<MeshFilter>();
    static int _nextRemain;
    static Transform _root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _shard = _crack = _web = _pane = null; _shardMesh = _quad = null;
        System.Array.Clear(_systems, 0, Systems);
        _decals.Clear(); _remains.Clear(); _next = _nextDecal = _nextRemain = 0; _root = null;
    }

    static Material Mat(ref Material m, string name) => m != null ? m : m = Resources.Load<Material>("Glass/" + name);
    public static Material Pane => Mat(ref _pane, "Pane");
    public static Material Clear => Resources.Load<Material>("Glass/Clear");

    static Transform Root
    {
        get
        {
            if (_root == null) { _root = new GameObject("GlassFX").transform; _decals.Clear(); _remains.Clear(); System.Array.Clear(_systems, 0, Systems); }
            return _root;
        }
    }

    static Mesh ShardMesh
    {
        get
        {
            if (_shardMesh != null) return _shardMesh;
            _shardMesh = new Mesh { name = "GlassShard" };
            _shardMesh.SetVertices(new[] { new Vector3(-0.5f, -0.4f, 0f), new Vector3(0.55f, -0.25f, 0f), new Vector3(0.1f, 0.6f, 0f) });
            _shardMesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back });
            _shardMesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.up });
            _shardMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            return _shardMesh;
        }
    }

    static Mesh Quad
    {
        get
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "GlassDecal" };
            _quad.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
            _quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            _quad.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0);
            return _quad;
        }
    }

    static ParticleSystem System_()
    {
        var root = Root;
        int i = _next; _next = (_next + 1) % Systems;
        if (_systems[i] != null) return _systems[i];
        var go = new GameObject("Shards");
        go.transform.SetParent(root, false);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 260; main.gravityModifier = 1.5f; main.startLifetime = 2.6f; main.startSpeed = 0f;
        main.startRotation3D = true;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.collision;
        col.enabled = true; col.type = ParticleSystemCollisionType.World; col.quality = ParticleSystemCollisionQuality.Low;
        col.bounce = 0.22f; col.dampen = 0.4f; col.lifetimeLoss = 0.06f; col.radiusScale = 0.3f;
        col.collidesWith = LayerMask.GetMask("Environment", "Default");
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-9f, 9f); rot.y = new ParticleSystem.MinMaxCurve(-9f, 9f); rot.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = ShardMesh;
        r.sharedMaterial = Mat(ref _shard, "Shard");
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        go.SetActive(true);
        ps.Play();
        return _systems[i] = ps;
    }

    /// <summary>A burst of falling pieces across a rectangle. Tempered glass: many small cubes. Ordinary: fewer, longer shards.</summary>
    public static void Shards(Vector3 corner, Vector3 u, Vector3 v, Vector3 push, bool cubes, float near)
    {
        float area = u.magnitude * v.magnitude;
        int n = Mathf.Clamp(Mathf.RoundToInt(area * (cubes ? 150f : 55f)), cubes ? 24 : 12, cubes ? 150 : 90);
        if (QualityTiers.Current == GameSettings.Quality.Low) n /= 2;
        // Far off nobody counts them.
        if (near > 40f) n /= 3;
        var ps = System_();
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < n; i++)
        {
            e.position = corner + u * Random.value + v * Random.value;
            e.velocity = push * Random.Range(0.3f, 2.4f) + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.2f, 0.6f), Random.Range(-0.5f, 0.5f));
            e.startSize = cubes ? Random.Range(0.012f, 0.03f) : Random.Range(0.03f, 0.13f);
            e.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            e.startColor = new Color32(255, 255, 255, 255);
            e.startLifetime = Random.Range(1.2f, 2.6f);
            ps.Emit(e, 1);
        }
    }

    /// <summary>A star of cracks or a spider web, laid on the glass at a point.</summary>
    public static void Crack(Vector3 at, Vector3 normal, float size, bool web)
    {
        var root = Root;
        GameObject go;
        if (_decals.Count < Decals)
        {
            go = new GameObject("Crack");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            _decals.Add(go);
        }
        else { go = _decals[_nextDecal]; _nextDecal = (_nextDecal + 1) % Decals; }
        go.GetComponent<MeshRenderer>().sharedMaterial = web ? Mat(ref _web, "Web") : Mat(ref _crack, "Crack");
        go.transform.SetPositionAndRotation(at + normal * 0.004f, Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        go.transform.localScale = Vector3.one * size;
        go.SetActive(true);
    }

    /// <summary>Takes any cracks off a rectangle: the pane they were on has gone.</summary>
    public static void ClearCracks(Vector3 centre, float radius)
    {
        foreach (var d in _decals)
            if (d != null && d.activeSelf && (d.transform.position - centre).sqrMagnitude < radius * radius) d.SetActive(false);
    }

    /// <summary>The jagged teeth a broken pane leaves in its frame.</summary>
    public static void Remains(Vector3 corner, Vector3 u, Vector3 v, Vector3 normal)
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

        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        Vector3 c = corner + normal * 0.006f, w = u, h = v;
        Teeth(verts, tris, uvs, c, w, h);                 // along the sill, into the opening
        Teeth(verts, tris, uvs, c + w + h, -w, -h);       // the head
        Teeth(verts, tris, uvs, c + h, -h, w);            // one side
        Teeth(verts, tris, uvs, c + w, h, -w);            // the other
        var mesh = mf.sharedMesh;
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        var normals = new Vector3[verts.Count];
        for (int i = 0; i < normals.Length; i++) normals[i] = normal;
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mf.gameObject.SetActive(true);
    }

    static void Teeth(List<Vector3> vs, List<int> ts, List<Vector2> uvs, Vector3 from, Vector3 along, Vector3 inward)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(along.magnitude / 0.14f), 3, 12);
        float x = 0f;
        for (int i = 0; i < n && x < 1f; i++)
        {
            float width = Random.Range(0.6f, 1.6f) / n;
            float tip = x + width * Random.Range(0.2f, 0.8f);
            float depth = Random.value < 0.25f ? 0f : Random.Range(0.04f, 0.32f);
            float end = Mathf.Min(1f, x + width);
            if (depth > 0f)
            {
                int s = vs.Count;
                vs.Add(from + along * x); vs.Add(from + along * end); vs.Add(from + along * tip + inward * depth);
                uvs.Add(new Vector2(x, 0f)); uvs.Add(new Vector2(end, 0f)); uvs.Add(new Vector2(tip, depth));
                ts.Add(s); ts.Add(s + 1); ts.Add(s + 2); ts.Add(s); ts.Add(s + 2); ts.Add(s + 1);
            }
            x = end;
        }
    }

    public static void Play(AudioClip clip, Vector3 at, float volume)
    {
        if (clip != null) AudioSource.PlayClipAtPoint(clip, at, volume);
    }
}
