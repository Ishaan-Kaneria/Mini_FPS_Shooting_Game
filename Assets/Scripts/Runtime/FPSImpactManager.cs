using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>What a round has struck, for what it throws up and the mark it leaves.</summary>
public enum ImpactSurface { Generic, Concrete, Metal, Wood, Glass, Sand, Snow, Ice, Flesh }

/// <summary>
/// What a round does to what it strikes: a mark on the surface, and what the surface throws up -- concrete and stone a
/// hole and a burst of grey-beige dust drifting down, metal a bright dent, a spray of orange sparks and a flash of light
/// for a thirtieth of a second, anything else a dark mark and a puff. Called with the hit from every gun's raycast
/// (Weapon, EnemyAI, TechnicalTruck); bodies are left to their own blood.
///
/// The surface comes from the struck face where a mesh carries many materials (a building's 64 m chunk: a shutter in a
/// plaster wall is metal), else the collider's physics material (the builders give the town's colliders Surface_*
/// ones, FPSKitImpactAssets), else its surface tag, as every arena has.
///
/// Nothing is made or destroyed per shot, so memory stays flat however long the firing goes on:
///   * the marks are quads in one mesh, a ring of <see cref="ImpactSettings.maxDecals"/>; the oldest gives way to the
///     newest, each fades over its last seconds and is gone at 30 (one draw call for all of them);
///   * the dust, chips and sparks are emitted into three particle systems that live as long as the scene;
///   * the flash is one of three pooled point lights, no shadows.
/// One per scene, made on first use; a mark is not left on anything with a Rigidbody (a car driving off would leave it
/// hanging in the air).
/// </summary>
[DisallowMultipleComponent]
public class FPSImpactManager : MonoBehaviour
{
    static FPSImpactManager _instance;
    static readonly Dictionary<Material, ImpactSurface> _byMaterial = new Dictionary<Material, ImpactSurface>();
    static readonly Dictionary<PhysicsMaterial, ImpactSurface> _byPhysics = new Dictionary<PhysicsMaterial, ImpactSurface>();
    static readonly List<Material> _faceMaterials = new List<Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _byMaterial.Clear();
        _byPhysics.Clear();
        _faceMaterials.Clear();
    }

    /// <summary>The scene's manager, made on first use (it goes with the scene).</summary>
    public static FPSImpactManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = FindAnyObjectByType<FPSImpactManager>();
            if (_instance == null) _instance = new GameObject("FPSImpactManager").AddComponent<FPSImpactManager>();
            return _instance;
        }
    }

    ImpactSettings _s;
    bool _built;
    float _clock;

    // The marks: four corners each, in one mesh.
    Mesh _mesh;
    Vector3[] _verts, _normals;
    Vector2[] _uvs;
    Color32[] _colors;
    float[] _born;
    byte[] _alpha;
    int _next, _live;
    bool _geometryDirty, _colorsDirty;

    ParticleSystem _dust, _chips, _sparks;
    Light[] _flashes;
    float[] _flashOff;

    /// <summary>Marks on the walls now (for the checks).</summary>
    public int LiveMarks => _live;
    /// <summary>Flashes lit now (for the checks).</summary>
    public int LitFlashes { get { int n = 0; if (_flashes != null) foreach (var l in _flashes) if (l != null && l.enabled) n++; return n; } }
    /// <summary>Particles in the air now (for the checks).</summary>
    public int LiveParticles => (_dust != null ? _dust.particleCount : 0) + (_chips != null ? _chips.particleCount : 0) + (_sparks != null ? _sparks.particleCount : 0);
    /// <summary>Most marks there can be at once.</summary>
    public int MarkCapacity => _born != null ? _born.Length : 0;

    // ------------------------------------------------------------------
    // What was struck
    // ------------------------------------------------------------------
    /// <summary>What <paramref name="hit"/> struck: the face's material on a mesh of many, the physics material, the tag.</summary>
    public static ImpactSurface SurfaceOf(RaycastHit hit)
    {
        var col = hit.collider;
        if (col == null) return ImpactSurface.Generic;
        if (col.CompareTag("Flesh")) return ImpactSurface.Flesh;
        if (hit.triangleIndex >= 0 && col is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.subMeshCount > 1 &&
            col.TryGetComponent(out MeshRenderer renderer))
        {
            var face = FaceMaterial(mc.sharedMesh, renderer, hit.triangleIndex);
            if (face != null)
            {
                var s = OfMaterial(face);
                if (s != ImpactSurface.Generic) return s;
            }
        }
        var physics = col.sharedMaterial;
        if (physics != null)
        {
            var s = OfPhysics(physics);
            if (s != ImpactSurface.Generic) return s;
        }
        return OfTag(col);
    }

    /// <summary>The tag the impact library plays a sound for (its fallback for anything it has no entry for).</summary>
    public static string LibraryTag(ImpactSurface s, string fallback)
    {
        switch (s)
        {
            case ImpactSurface.Concrete: case ImpactSurface.Glass: return "Concrete";
            case ImpactSurface.Metal: return "Metal";
            case ImpactSurface.Wood: return "Wood";
            case ImpactSurface.Sand: return "Sand";
            case ImpactSurface.Snow: return "Snow";
            case ImpactSurface.Ice: return "Ice";
            case ImpactSurface.Flesh: return "Flesh";
            default: return fallback;
        }
    }

    static Material FaceMaterial(Mesh mesh, Renderer renderer, int triangle)
    {
        int index = triangle * 3;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var sm = mesh.GetSubMesh(i);
            if (index < sm.indexStart || index >= sm.indexStart + sm.indexCount) continue;
            renderer.GetSharedMaterials(_faceMaterials);
            return i < _faceMaterials.Count ? _faceMaterials[i] : null;
        }
        return null;
    }

    /// <summary>A render material's surface by its name (cached): wood, glass and metal are told apart; the rest is left to
    /// the collider.</summary>
    static ImpactSurface OfMaterial(Material m)
    {
        if (_byMaterial.TryGetValue(m, out var s)) return s;
        string n = m.name.ToLowerInvariant();
        if (n.Contains("wood") || n.Contains("plank") || n.Contains("beam") || n.Contains("pine") || n.Contains("pallet") || n.Contains("timber"))
            s = ImpactSurface.Wood;
        else if (n.Contains("glass")) s = ImpactSurface.Glass;
        else if (n.Contains("shutter") || n.Contains("steel") || n.Contains("alumin") || n.Contains("iron") || n.Contains("metal") ||
                 n.Contains("rebar") || n.Contains("frame") || n.Contains("signal") || n.Contains("tank") || n.Contains("dish") ||
                 n.Contains("solar") || n.Contains("gate") || n.Contains("rmcar") || n.Contains("chrome"))
            s = ImpactSurface.Metal;
        else s = ImpactSurface.Generic;
        _byMaterial[m] = s;
        return s;
    }

    static ImpactSurface OfPhysics(PhysicsMaterial m)
    {
        if (_byPhysics.TryGetValue(m, out var s)) return s;
        string n = m.name;
        s = n.EndsWith("Concrete") ? ImpactSurface.Concrete : n.EndsWith("Metal") ? ImpactSurface.Metal : n.EndsWith("Wood") ? ImpactSurface.Wood
          : n.EndsWith("Glass") ? ImpactSurface.Glass : n.EndsWith("Sand") ? ImpactSurface.Sand : n.EndsWith("Snow") ? ImpactSurface.Snow
          : n.EndsWith("Ice") ? ImpactSurface.Ice : ImpactSurface.Generic;
        _byPhysics[m] = s;
        return s;
    }

    static ImpactSurface OfTag(Collider c)
    {
        if (c.CompareTag("Concrete")) return ImpactSurface.Concrete;
        if (c.CompareTag("Metal")) return ImpactSurface.Metal;
        if (c.CompareTag("Wood")) return ImpactSurface.Wood;
        if (c.CompareTag("Sand")) return ImpactSurface.Sand;
        if (c.CompareTag("Snow")) return ImpactSurface.Snow;
        if (c.CompareTag("Ice")) return ImpactSurface.Ice;
        return ImpactSurface.Generic;
    }

    // ------------------------------------------------------------------
    // The impact
    // ------------------------------------------------------------------
    /// <summary>A round struck <paramref name="hit"/>: its mark, and what the surface throws up.</summary>
    public void HandleImpact(RaycastHit hit) => HandleImpact(hit, SurfaceOf(hit));

    /// <summary>As <see cref="HandleImpact(RaycastHit)"/>, the surface already known.</summary>
    public void HandleImpact(RaycastHit hit, ImpactSurface surface)
    {
        if (hit.collider == null || surface == ImpactSurface.Flesh || !Built) return;
        Vector3 p = hit.point, n = hit.normal.sqrMagnitude > 0.5f ? hit.normal : Vector3.up;
        bool mark = hit.rigidbody == null;
        var s = _s;
        switch (surface)
        {
            case ImpactSurface.Concrete:
                if (mark) AddMark(p, n, Random.value < 0.5f ? 0 : 3, s.concreteHoleSize, new Color32(226, 222, 214, 255));
                Burst(_dust, p, n, s.concreteDustCount, new Vector2(0.4f, 1.6f), 0.75f, s.dustLifetime, new Vector2(0.05f, 0.14f), s.concreteDust);
                Burst(_chips, p, n, 5, new Vector2(2.5f, 5.5f), 0.8f, new Vector2(0.3f, 0.6f), new Vector2(0.012f, 0.028f), s.concreteDust * 0.7f);
                break;
            case ImpactSurface.Metal:
                if (mark) AddMark(p, n, 1, s.metalDentSize, new Color32(255, 255, 255, 255));
                Burst(_sparks, p, n, s.sparkCount, s.sparkSpeed, 0.9f, new Vector2(0.12f, 0.35f), new Vector2(0.012f, 0.024f), Color.white);
                Flash(p + n * 0.08f);
                break;
            case ImpactSurface.Wood:
                if (mark) AddMark(p, n, 2, s.genericMarkSize * 0.85f, new Color32(150, 120, 95, 255));
                Burst(_dust, p, n, 6, new Vector2(0.4f, 1.3f), 0.7f, s.dustLifetime * 0.7f, new Vector2(0.04f, 0.1f), s.woodDust);
                Burst(_chips, p, n, 7, new Vector2(2f, 4.5f), 0.9f, new Vector2(0.3f, 0.7f), new Vector2(0.015f, 0.035f), s.woodDust);
                break;
            case ImpactSurface.Glass:
                if (mark) AddMark(p, n, 2, s.genericMarkSize * 0.7f, new Color32(190, 195, 200, 255));
                Burst(_chips, p, n, 8, new Vector2(1.5f, 4f), 1f, new Vector2(0.3f, 0.6f), new Vector2(0.01f, 0.022f), new Color(0.9f, 0.95f, 1f, 0.9f));
                break;
            case ImpactSurface.Sand:
                Burst(_dust, p, n, 14, new Vector2(0.6f, 2.2f), 0.6f, s.dustLifetime * 1.2f, new Vector2(0.08f, 0.2f), s.sandDust);
                break;
            case ImpactSurface.Snow:
            case ImpactSurface.Ice:
                Burst(_dust, p, n, 12, new Vector2(0.5f, 2f), 0.7f, s.dustLifetime, new Vector2(0.07f, 0.18f), s.snowDust);
                if (surface == ImpactSurface.Ice) Burst(_chips, p, n, 6, new Vector2(2f, 4f), 0.9f, new Vector2(0.3f, 0.5f), new Vector2(0.01f, 0.02f), s.snowDust);
                break;
            default:
                if (mark) AddMark(p, n, 2, s.genericMarkSize, new Color32(205, 202, 198, 255));
                Burst(_dust, p, n, 6, new Vector2(0.3f, 1.1f), 0.7f, s.dustLifetime * 0.8f, new Vector2(0.04f, 0.1f), s.genericDust);
                break;
        }
    }

    /// <summary>Moves the manager's clock on by <paramref name="seconds"/>, fading the marks and putting out flashes (the
    /// checks drive it without playing).</summary>
    public void Advance(float seconds)
    {
        if (!Built) return;
        Tick(seconds);
    }

    void LateUpdate()
    {
        if (_built) Tick(Time.deltaTime);
    }

    void Tick(float dt)
    {
        _clock += dt;
        if (_live > 0) FadeMarks();
        if (_flashes != null)
            for (int i = 0; i < _flashes.Length; i++)
                if (_flashes[i] != null && _flashes[i].enabled && _clock >= _flashOff[i]) _flashes[i].enabled = false;
        if (_mesh == null) return;
        if (_geometryDirty)
        {
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.uv = _uvs;
            _geometryDirty = false;
            _colorsDirty = true;
        }
        if (_colorsDirty)
        {
            _mesh.colors32 = _colors;
            _colorsDirty = false;
        }
    }

    // ------------------------------------------------------------------
    // Marks
    // ------------------------------------------------------------------
    /// <summary>A mark: one of the atlas's four cells, centred on <paramref name="p"/>, turned at random about the normal.</summary>
    void AddMark(Vector3 p, Vector3 n, int cell, float size, Color32 tint)
    {
        if (_born == null) return;                                      // no mark material: dust and sparks only
        int count = _born.Length;
        int i = _next;
        _next = (_next + 1) % count;
        if (_alpha[i] == 0) _live++;
        _born[i] = _clock;
        _alpha[i] = 255;

        Vector3 any = Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.forward;
        Vector3 up = Quaternion.AngleAxis(Random.Range(0f, 360f), n) * Vector3.ProjectOnPlane(any, n).normalized;
        Vector3 right = Vector3.Cross(n, up);
        float half = size * Random.Range(0.75f, 1.25f) * 0.5f;
        Vector3 c = p + n * _s.decalOffset;
        int v = i * 4;
        // Bottom left, top left, top right, bottom right as seen from in front: clockwise, so facing out.
        _verts[v] = c - right * half - up * half;
        _verts[v + 1] = c - right * half + up * half;
        _verts[v + 2] = c + right * half + up * half;
        _verts[v + 3] = c + right * half - up * half;
        float u0 = (cell % 2) * 0.5f + 0.004f, v0 = (cell / 2) * 0.5f + 0.004f, u1 = u0 + 0.492f, v1 = v0 + 0.492f;
        _uvs[v] = new Vector2(u0, v0); _uvs[v + 1] = new Vector2(u0, v1); _uvs[v + 2] = new Vector2(u1, v1); _uvs[v + 3] = new Vector2(u1, v0);
        for (int k = 0; k < 4; k++) { _normals[v + k] = n; _colors[v + k] = tint; }
        _geometryDirty = true;
    }

    void FadeMarks()
    {
        float life = _s.decalLifetime, from = life - Mathf.Clamp(_s.decalFade, 0.01f, life);
        for (int i = 0; i < _born.Length; i++)
        {
            if (_alpha[i] == 0) continue;
            float age = _clock - _born[i];
            if (age < from) continue;
            byte a = age >= life ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(255f * (life - age) / (life - from)), 1, 255);
            if (a == _alpha[i]) continue;
            _alpha[i] = a;
            int v = i * 4;
            for (int k = 0; k < 4; k++) _colors[v + k].a = a;
            _colorsDirty = true;
            if (a == 0)
            {
                // Gone: its corners together, so the quad draws nothing.
                for (int k = 1; k < 4; k++) _verts[v + k] = _verts[v];
                _live--;
                _geometryDirty = true;
            }
        }
    }

    // ------------------------------------------------------------------
    // Dust, chips, sparks, flashes
    // ------------------------------------------------------------------
    void Burst(ParticleSystem ps, Vector3 at, Vector3 n, int count, Vector2 speed, float spread, Vector2 life, Vector2 size, Color color)
    {
        if (ps == null || count <= 0) return;
        var e = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = color };
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = (n + Random.insideUnitSphere * spread).normalized;
            if (Vector3.Dot(dir, n) < 0.05f) dir = (dir + n).normalized;
            e.position = at + n * 0.02f;
            e.velocity = dir * Random.Range(speed.x, speed.y);
            e.startLifetime = Random.Range(life.x, life.y);
            e.startSize = Random.Range(size.x, size.y);
            e.rotation = Random.Range(0f, 360f);
            ps.Emit(e, 1);
        }
    }

    void Flash(Vector3 at)
    {
        if (_flashes == null) return;
        int pick = 0;
        for (int i = 1; i < _flashes.Length; i++) if (_flashOff[i] < _flashOff[pick]) pick = i;
        var l = _flashes[pick];
        if (l == null) return;
        l.transform.position = at;
        l.enabled = true;
        _flashOff[pick] = _clock + _s.flashDuration;
    }

    // ------------------------------------------------------------------
    // Built on first use
    // ------------------------------------------------------------------
    bool Built
    {
        get
        {
            if (_built) return _s != null;
            _built = true;
            _s = ImpactSettings.Active;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            BuildMarks();
            _dust = BuildParticles("Dust", _s.dustMaterial, 600, false, 0.06f, 2.6f, 2.2f, true);
            _chips = BuildParticles("Chips", _s.dustMaterial, 240, false, 1.2f, 0.4f, 1f, true);
            _sparks = BuildParticles("Sparks", _s.sparkMaterial, 360, true, 1.3f, 0.6f, 1f, false);
            _flashes = new Light[3];
            _flashOff = new float[3];
            for (int i = 0; i < _flashes.Length; i++)
            {
                var go = new GameObject("Flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.6f, 0.25f);
                l.intensity = _s.flashIntensity;
                l.range = _s.flashRange;
                l.enabled = false;
                _flashes[i] = l;
            }
            return _s != null;
        }
    }

    void BuildMarks()
    {
        if (_s.decalMaterial == null) return;
        int n = Mathf.Max(16, _s.maxDecals);
        _verts = new Vector3[n * 4]; _normals = new Vector3[n * 4]; _uvs = new Vector2[n * 4]; _colors = new Color32[n * 4];
        _born = new float[n]; _alpha = new byte[n];
        var tris = new int[n * 6];
        for (int i = 0; i < n; i++)
        {
            int v = i * 4, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            _normals[v] = _normals[v + 1] = _normals[v + 2] = _normals[v + 3] = Vector3.up;
        }
        _mesh = new Mesh { name = "ImpactMarks" };
        _mesh.MarkDynamic();
        _mesh.vertices = _verts; _mesh.normals = _normals; _mesh.uv = _uvs; _mesh.colors32 = _colors;
        _mesh.triangles = tris;
        // Marks can be anywhere: never culled as a whole (its quads are what is drawn, a few hundred at most).
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        var go = new GameObject("Marks");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = _s.decalMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.allowOcclusionWhenDynamic = false;
    }

    ParticleSystem BuildParticles(string name, Material mat, int max, bool stretch, float gravity, float drag, float grow, bool lit)
    {
        if (mat == null) return null;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startSpeed = 0f;
        main.gravityModifier = gravity;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        if (drag > 0f)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
        }
        if (grow > 1f)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
        }
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(stretch ? new Color(1f, 0.55f, 0.3f) : Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(stretch ? 0.8f : 0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        r.velocityScale = stretch ? 0.03f : 0f;
        r.lengthScale = stretch ? 1.5f : 1f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = lit;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ps.Play();
        return ps;
    }
}
