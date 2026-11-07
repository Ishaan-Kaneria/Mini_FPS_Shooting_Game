using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// The sun's shadows spent where they are seen (his choice of 2026-11-07, options 1 and 2 of the lighting list): a GTA-style
/// budget. Up close nothing changes: every beam, air conditioner, sign, awning and shutter casts its full real shadow.
///
///   * <b>Small things</b> (a renderer whose bounds are at most <see cref="propMaxSize"/> across: a crate, a drum, an AC
///     unit, a chair) cast only within <see cref="propRadius"/> of the eye. Past it their shadow is a few texels of the far
///     cascade, and there were over a thousand of them drawn into the shadow map every frame.
///   * <b>Big merged meshes</b> (the town's 64 m building blocks, landmarks) cast with their own full geometry within
///     <see cref="nearRadius"/> of the eye (measured from their bounds, so the street you stand in is always near). Further
///     away a stand-in casts instead: one draw, the same mesh with its fine work (sills, frames, cornices, every triangle
///     under <see cref="standInMinArea"/> m²) left out. It has the building's real outline: boxes were tried on paper and either
///     poked out of sunlit walls (darkening them) or left a lit strip at the foot of the shady side.
///   * <b>Low wide meshes</b> (pavements and kerbs, under <see cref="lowHeight"/> tall) and the facades' <c>Detail_</c> meshes
///     cast nothing at distance: a kerb's shadow at 60 m is under a pixel.
///
/// Gathered when a scene loads (the desert's room districts load after it: each load adds what is new), before play, so
/// nothing is built during a level. A slice of the small things is checked each frame. Applies only in arenas (a scene with
/// an "Arena" root) and only while the tier draws shadows. <see cref="Active"/> off puts every renderer back as it was (the
/// walk's before/after in one build).
/// </summary>
[DisallowMultipleComponent]
public class ShadowBudget : MonoBehaviour
{
    [Header("Small things")]
    [Tooltip("Metres from the eye within which small things cast the sun's shadow.")]
    [Min(5f)] public float propRadius = 25f;
    [Tooltip("The largest a renderer may be (its bounds' longest side, metres) to count as a small thing.")]
    [Min(0.5f)] public float propMaxSize = 3f;
    [Tooltip("Frames over which every small thing is checked once (at 60 fps, 15 is four times a second).")]
    [Range(1, 60)] public int slices = 15;

    [Header("Big meshes")]
    [Tooltip("Metres from a big mesh's bounds within which it casts with its own full geometry; past it the stand-in casts.")]
    [Min(10f)] public float nearRadius = 40f;
    [Tooltip("Triangles smaller than this (square metres) are left out of a stand-in.")]
    [Min(0f)] public float standInMinArea = 0.12f;
    [Tooltip("A big mesh under this height (metres) casts nothing past the near radius: pavements, kerbs, low walls.")]
    [Min(0f)] public float lowHeight = 1.2f;
    [Tooltip("Seconds between checks of the big meshes.")]
    [Range(0.05f, 1f)] public float bigInterval = 0.2f;

    /// <summary>Off: everything casts as the scene was built (set by the walk's -fpskitlegacy and its before/after views).</summary>
    public static bool Active = true;
    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_hooked) SceneManager.sceneLoaded -= OnSceneLoaded;
        _hooked = false;
        Active = true;
        _standInMaterial = null;
        _instance = null;
        Toggled = 0;
    }

    static ShadowBudget _instance;
    /// <summary>Renderers whose shadow casting this switched, since the walk last read it (its slow-frame report).</summary>
    public static int Toggled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!_hooked) { SceneManager.sceneLoaded += OnSceneLoaded; _hooked = true; }
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (_instance == null)
        {
            // Lives in the arena's own scene (the one with the "Arena" root): goes when the level is left.
            bool arena = false;
            foreach (var root in scene.GetRootGameObjects()) if (root.name == "Arena") { arena = true; break; }
            if (!arena) return;
            _instance = new GameObject("ShadowBudget").AddComponent<ShadowBudget>();
            SceneManager.MoveGameObjectToScene(_instance.gameObject, scene);
        }
        _instance.GatherNow(scene);
    }

    struct Prop { public Renderer r; public Vector3 centre; public ShadowCastingMode mode; }
    struct Big { public Renderer r; public Bounds bounds; public ShadowCastingMode mode; public Renderer standIn; public bool near; }

    readonly List<Prop> _props = new List<Prop>();
    readonly List<Big> _bigs = new List<Big>();
    HashSet<Renderer> _listed;
    int _next;
    float _nextBig;
    bool _applied;

    static Material _standInMaterial;
    static Material StandInMaterial
    {
        get
        {
            if (_standInMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) return null;
                _standInMaterial = new Material(shader) { name = "ShadowStandIn" };
                _standInMaterial.SetFloat("_Cull", 0f);     // both sides: a stand-in has lost the faces that closed it
            }
            return _standInMaterial;
        }
    }

    public int PropCount => _props.Count;
    public int BigCount => _bigs.Count;
    public int StandInCount { get { int n = 0; foreach (var b in _bigs) if (b.standIn != null) n++; return n; } }
    /// <summary>One line for the walk's report.</summary>
    public string Summary { get; private set; } = "";

    static bool ShadowsDrawn =>
        GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp && urp.supportsMainLightShadows && urp.shadowDistance > 0f;

    long _kept, _dropped;

    void GatherNow(Scene scene)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (_listed == null) _listed = new HashSet<Renderer>();
        int[] skipLayers = { LayerMask.NameToLayer("FirstPerson"), LayerMask.NameToLayer("Player"), LayerMask.NameToLayer("Enemy"), LayerMask.NameToLayer("Civilian") };
        var found = new List<MeshRenderer>();
        foreach (var root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));
        foreach (var r in found)
        {
            if (r == null || !_listed.Add(r)) continue;
            var mode = r.shadowCastingMode;
            if (mode == ShadowCastingMode.Off || mode == ShadowCastingMode.ShadowsOnly) continue;
            if (System.Array.IndexOf(skipLayers, r.gameObject.layer) >= 0) continue;
            // Anything that moves (cars, loose props, bodies) keeps its shadow: its bounds would not stay where listed.
            if (r.GetComponentInParent<Rigidbody>(true) != null || r.GetComponentInParent<Animator>(true) != null) continue;
            var b = r.bounds;
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (size <= propMaxSize) { _props.Add(new Prop { r = r, centre = b.center, mode = mode }); continue; }
            if (size < 16f) continue;   // a house, a tree, a car: casts as built
            var big = new Big { r = r, bounds = b, mode = mode, near = true };
            bool detail = r.name.StartsWith("Detail_");
            if (!detail && b.size.y >= lowHeight) big.standIn = MakeStandIn(r, ref _kept, ref _dropped);
            // A big mesh that could not be read keeps casting as built (never a building with no shadow).
            if (!detail && b.size.y >= lowHeight && big.standIn == null) continue;
            _bigs.Add(big);
        }
        _applied = false;
        Summary = $"shadow budget: {_props.Count} small things cast within {propRadius:0} m, {_bigs.Count} big meshes within {nearRadius:0} m " +
                  $"({StandInCount} with stand-ins: {_kept} of {_kept + _dropped} triangles kept); {scene.name} gathered in {clock.ElapsedMilliseconds} ms";
        Debug.Log("[ShadowBudget] " + Summary);
    }

    /// <summary>The stand-in: one submesh of the big mesh's triangles over <see cref="standInMinArea"/>, shadows only.</summary>
    Renderer MakeStandIn(MeshRenderer r, ref long kept, ref long dropped)
    {
        var mat = StandInMaterial;
        if (mat == null) return null;
        // The mesh as authored. In a player the static batcher hands the renderer a combined mesh, so the collider's copy
        // (the same mesh, never batched) is read first.
        Mesh src = null; Matrix4x4 toWorld = r.transform.localToWorldMatrix; int first = 0, count = 0;
        var col = r.GetComponent<MeshCollider>();
        var mf = r.GetComponent<MeshFilter>();
        if (col != null && col.sharedMesh != null && col.sharedMesh.isReadable && mf != null && (mf.sharedMesh == col.sharedMesh || r.isPartOfStaticBatch))
        { src = col.sharedMesh; count = src.subMeshCount; }
        else if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
        {
            src = mf.sharedMesh;
            if (r.isPartOfStaticBatch)
            {
                // Combined: the renderer's submeshes are a range of the batch, already in world space.
                first = r.subMeshStartIndex; count = r.sharedMaterials.Length; toWorld = Matrix4x4.identity;
            }
            else count = src.subMeshCount;
        }
        if (src == null || count <= 0) return null;

        var verts = src.vertices;
        var map = new Dictionary<int, int>();
        var outVerts = new List<Vector3>();
        var outTris = new List<int>();
        float minArea2 = standInMinArea * standInMinArea * 4f;   // |cross|² = (2 area)²
        for (int s = first; s < first + count && s < src.subMeshCount; s++)
        {
            if (src.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = src.GetTriangles(s);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < minArea2) { dropped++; continue; }
                kept++;
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t + k];
                    if (!map.TryGetValue(vi, out int o)) { o = outVerts.Count; outVerts.Add(verts[vi]); map[vi] = o; }
                    outTris.Add(o);
                }
            }
        }
        if (outTris.Count == 0) return null;
        var mesh = new Mesh { name = r.name + "_ShadowStandIn" };
        if (outVerts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(outVerts);
        mesh.SetTriangles(outTris, 0, true);
        mesh.UploadMeshData(true);

        var go = new GameObject(r.name + "_ShadowStandIn");
        go.layer = r.gameObject.layer;
        SceneManager.MoveGameObjectToScene(go, r.gameObject.scene);
        go.transform.SetPositionAndRotation(toWorld.GetColumn(3), toWorld.rotation);
        go.transform.localScale = toWorld.lossyScale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.enabled = false;
        return mr;
    }

    void OnDisable() => Restore();
    void OnDestroy() { Restore(); if (_instance == this) _instance = null; }

    /// <summary>Every renderer back as it was built, the stand-ins off.</summary>
    void Restore()
    {
        if (!_applied) return;
        _applied = false;
        foreach (var p in _props) if (p.r != null) p.r.shadowCastingMode = p.mode;
        for (int i = 0; i < _bigs.Count; i++)
        {
            var b = _bigs[i];
            if (b.r != null) b.r.shadowCastingMode = b.mode;
            if (b.standIn != null) b.standIn.enabled = false;
            b.near = true; _bigs[i] = b;
        }
    }

    void Update()
    {
        if (!Active || !ShadowsDrawn) { Restore(); return; }
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        bool first = !_applied;
        _applied = true;

        // Small things: a slice a frame (all of them on the first frame, so none casts from afar for a quarter second).
        if (_props.Count > 0)
        {
            float r2 = propRadius * propRadius;
            int per = first ? _props.Count : Mathf.Max(1, (_props.Count + slices - 1) / slices);
            for (int n = 0; n < per; n++)
            {
                if (_next >= _props.Count) _next = 0;
                var p = _props[_next++];
                if (p.r == null) continue;
                var want = (p.centre - eye).sqrMagnitude <= r2 ? p.mode : ShadowCastingMode.Off;
                if (p.r.shadowCastingMode != want) { p.r.shadowCastingMode = want; Toggled++; }
            }
        }

        if (!first && Time.unscaledTime < _nextBig) return;
        _nextBig = Time.unscaledTime + bigInterval;
        float n2 = nearRadius * nearRadius;
        for (int i = 0; i < _bigs.Count; i++)
        {
            var b = _bigs[i];
            if (b.r == null) continue;
            bool near = (b.bounds.ClosestPoint(eye) - eye).sqrMagnitude <= n2;
            if (!first && near == b.near) continue;
            b.near = near;
            b.r.shadowCastingMode = near ? b.mode : ShadowCastingMode.Off;
            Toggled++;
            // The stand-in only while the real mesh is drawn at all (a room district switched off takes its stand-in with it).
            if (b.standIn != null) b.standIn.enabled = !near && b.r.enabled && b.r.gameObject.activeInHierarchy;
            _bigs[i] = b;
        }
    }
}
