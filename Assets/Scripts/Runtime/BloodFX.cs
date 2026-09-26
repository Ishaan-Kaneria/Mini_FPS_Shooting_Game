using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The level's blood: sprays, mist and the splats a fight leaves on walls and floors.
///
/// **One particle system of each kind for the whole level, emitted into by code.** A
/// spray per hit as its own GameObject is what a firefight at 600rpm cannot afford in a
/// browser tab; <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/> into a
/// shared system costs a struct per droplet and nothing else.
///
/// **Splats are a fixed ring of quads.** Each is a flat mesh laid on the surface the
/// blood reached, found by a raycast along the round's path for the wall behind the
/// body and straight down for the floor under it. Past the budget for the quality tier
/// the oldest one is picked up and put down somewhere new, so the thousandth hit of a
/// level costs what the first did. Quads rather than URP decal projectors because the
/// decal renderer feature is not on every pipeline asset the kit ships with, and a quad
/// is the same cost on all of them.
///
/// Everything here lives under one object in the active scene. A scene change takes it
/// away, and the next hit finds the reference gone and builds it again -- the statics
/// are cleared on entering play mode for the reason every static in the kit is.
/// </summary>
public static class BloodFX
{
    /// <summary>A splat still spreading: the pool under a body, or a smear.</summary>
    struct Growth
    {
        public Transform target;
        public Vector3 from, to;
        public float start, duration;

        /// <summary>How front-loaded the spread is. Higher: most of it early, then a slow creep.</summary>
        public float power;
    }

    static BloodLibrary s_library;
    static Transform s_root;
    static ParticleSystem s_spray;
    static ParticleSystem s_mist;
    static readonly List<MeshRenderer> Decals = new List<MeshRenderer>();

    /// <summary>
    /// The pools under bodies, kept apart from the splats. Splats are recycled oldest-first
    /// once the tier's budget is spent; a pool marks where somebody died and stays until
    /// the level ends, like the body lying in it.
    /// </summary>
    static readonly List<MeshRenderer> Pools = new List<MeshRenderer>();

    /// <summary>Pools a level can hold before the oldest is reused. Far above any level's roster.</summary>
    const int PoolBudget = 160;
    static readonly List<Growth> Growing = new List<Growth>();
    static int s_next;
    static Mesh[] s_quads;
    static Mesh s_whole;
    static int s_surfaceMask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        s_library = null;
        s_root = null;
        s_spray = null;
        s_mist = null;
        Decals.Clear();
        Pools.Clear();
        Growing.Clear();
        s_next = 0;
        s_quads = null;
        s_whole = null;
        s_surfaceMask = 0;
    }

    /// <summary>The player's Blood setting. Off, a hit throws up a dull puff instead.</summary>
    public static bool Enabled => GameSettings.Blood;

    /// <summary>What blood can land on: the world, never a body or the player.</summary>
    static int SurfaceMask
    {
        get
        {
            if (s_surfaceMask == 0)
                s_surfaceMask = LayerMask.GetMask("Default", "Environment", "Backdrop");
            return s_surfaceMask;
        }
    }

    static int Budget
    {
        get
        {
            var budget = s_library != null ? s_library.decalBudget : new Vector3Int(48, 96, 160);

            switch (QualityTiers.Current)
            {
                case GameSettings.Quality.Low: return Mathf.Max(8, budget.x);
                case GameSettings.Quality.Medium: return Mathf.Max(8, budget.y);
                default: return Mathf.Max(8, budget.z);
            }
        }
    }

    /// <summary>Fewer droplets on Low. The splats are what stays; the spray is gone in a second.</summary>
    static float DropScale
    {
        get
        {
            switch (QualityTiers.Current)
            {
                case GameSettings.Quality.Low: return 0.45f;
                case GameSettings.Quality.Medium: return 0.75f;
                default: return 1f;
            }
        }
    }

    // ======================================================================
    // Hits
    // ======================================================================

    /// <summary>
    /// A round going into a body.
    ///
    /// <paramref name="severity"/> is 0 to 1 -- how much of the body that hit took -- and
    /// scales everything: a graze is a puff, a shotgun at a metre paints the wall.
    /// </summary>
    public static void Hit(BloodLibrary library, Vector3 point, Vector3 normal, Vector3 direction,
                           float severity)
    {
        if (!Ensure(library)) return;

        severity = Mathf.Clamp01(severity);

        if (direction.sqrMagnitude < 0.0001f) direction = -normal;
        direction.Normalize();
        if (normal.sqrMagnitude < 0.0001f) normal = -direction;

        if (!Enabled)
        {
            Mist(point, normal * 0.8f, 0.18f + 0.15f * severity, library.bloodlessColor, 3);
            return;
        }

        // The cloud at the wound, blown back out of it toward the shooter.
        Mist(point, normal * 1.1f + direction * 0.4f, 0.2f + 0.35f * severity,
             library.mistColor, 2 + Mathf.RoundToInt(3 * severity));

        // Droplets out of the far side, along the round. Most of what the eye reads as
        // "that went through him".
        int drops = Mathf.Max(3, Mathf.RoundToInt(library.dropsPerHit * (0.35f + 0.65f * severity) * DropScale));
        Spray(point, direction, 2.2f + 4.5f * severity, drops, library.dropColor);

        // A few back toward the shooter, slower.
        Spray(point, normal, 1.2f + 1.5f * severity, Mathf.Max(1, drops / 4), library.dropColor);

        // The wall behind. Chance and size both grow with the hit, so a pistol round
        // leaves a fleck and a heavy hit leaves the thing everyone remembers.
        if (Random.value < 0.35f + 0.6f * severity &&
            Physics.Raycast(point + direction * 0.3f, direction, out RaycastHit wall,
                            1.5f + 2.5f * severity, SurfaceMask, QueryTriggerInteraction.Ignore))
        {
            Splat(wall.point, wall.normal, Random.Range(0.3f, 0.55f) + 0.7f * severity, direction);
        }

        // And the floor under the wound.
        if (Random.value < 0.4f + 0.5f * severity)
            Drip(point + Random.insideUnitSphere * 0.35f, Random.Range(0.14f, 0.26f) + 0.25f * severity);
    }

    /// <summary>A drop or two falling from a wounded body onto the floor under it.</summary>
    public static void Drip(Vector3 from, float size)
    {
        if (!Enabled || !Ensure(s_library)) return;

        if (Physics.Raycast(from + Vector3.up * 0.2f, Vector3.down, out RaycastHit floor, 3f,
                            SurfaceMask, QueryTriggerInteraction.Ignore))
            Splat(floor.point, floor.normal, size, Vector3.zero);
    }

    /// <summary>
    /// A smear along the floor behind something dragging itself: stretched along the way
    /// it was going rather than round.
    /// </summary>
    public static void Smear(Vector3 at, Vector3 along, float width)
    {
        if (!Enabled || !Ensure(s_library)) return;

        if (!Physics.Raycast(at + Vector3.up * 0.4f, Vector3.down, out RaycastHit floor, 2f,
                             SurfaceMask, QueryTriggerInteraction.Ignore)) return;

        var decal = Place(floor.point, floor.normal, width, along, s_library.splatMaterial, Atlas());
        if (decal == null) return;

        var scale = decal.transform.localScale;
        scale.z *= 2.2f;
        decal.transform.localScale = scale;
    }

    /// <summary>
    /// The pool that spreads out under a body: quickly at first, then creeping outward for
    /// as long as <paramref name="seconds"/> says. It is never taken back while the level runs.
    /// </summary>
    public static void Pool(BloodLibrary library, Vector3 at, float size, float seconds)
    {
        if (!Enabled || !Ensure(library)) return;

        if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 3f,
                             SurfaceMask, QueryTriggerInteraction.Ignore)) return;

        var decal = Place(floor.point, floor.normal, size, Vector3.zero,
                          library.poolMaterial != null ? library.poolMaterial : library.splatMaterial,
                          library.poolMaterial != null ? Whole() : Atlas(), pool: true);
        if (decal == null) return;

        var full = decal.transform.localScale;

        Growing.Add(new Growth
        {
            target = decal.transform,
            from = full * 0.12f,
            to = full,
            start = Time.time,
            duration = Mathf.Max(0.1f, seconds),
            power = 4f
        });

        decal.transform.localScale = full * 0.12f;
    }

    // ======================================================================
    // Pieces
    // ======================================================================

    static void Spray(Vector3 at, Vector3 direction, float speed, int count, Color color)
    {
        if (s_spray == null) return;

        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

        for (int i = 0; i < count; i++)
        {
            Vector3 v = (direction + Random.insideUnitSphere * 0.45f).normalized * speed * Random.Range(0.4f, 1.1f);
            v += Vector3.up * Random.Range(0f, 1.2f);

            emit.position = at + Random.insideUnitSphere * 0.04f;
            emit.velocity = v;
            emit.startSize = Random.Range(0.018f, 0.05f);
            emit.startLifetime = Random.Range(0.35f, 0.8f);
            emit.startColor = color;

            s_spray.Emit(emit, 1);
        }
    }

    static void Mist(Vector3 at, Vector3 velocity, float size, Color color, int count)
    {
        if (s_mist == null) return;

        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

        for (int i = 0; i < count; i++)
        {
            emit.position = at + Random.insideUnitSphere * 0.05f;
            emit.velocity = velocity * Random.Range(0.5f, 1.2f) + Random.insideUnitSphere * 0.3f;
            emit.startSize = size * Random.Range(0.6f, 1.1f);
            emit.startLifetime = Random.Range(0.25f, 0.45f);
            emit.startColor = color;
            emit.rotation = Random.Range(0f, 360f);

            s_mist.Emit(emit, 1);
        }
    }

    /// <summary>A splat from the atlas, turned to a random angle, or along a direction.</summary>
    static void Splat(Vector3 point, Vector3 normal, float size, Vector3 along)
        => Place(point, normal, size, along, s_library.splatMaterial, Atlas());

    /// <summary>
    /// Lays a quad on a surface. Takes the next one from the ring, which past the budget
    /// is the oldest one in the level.
    /// </summary>
    static MeshRenderer Place(Vector3 point, Vector3 normal, float size, Vector3 along,
                              Material material, Mesh mesh, bool pool = false)
    {
        if (material == null || s_root == null) return null;

        MeshRenderer decal = null;
        var ring = pool ? Pools : Decals;
        int budget = pool ? PoolBudget : Budget;

        // Destroyed from outside (a scene change mid-fight) leaves holes; drop them.
        for (int i = ring.Count - 1; i >= 0; i--)
            if (ring[i] == null) ring.RemoveAt(i);

        if (ring.Count < budget)
        {
            var go = new GameObject(pool ? "BloodPool" : "Blood");
            go.layer = 2;   // Ignore Raycast: nothing should ever hit a splat
            go.transform.SetParent(s_root, false);
            go.AddComponent<MeshFilter>();
            decal = go.AddComponent<MeshRenderer>();
            decal.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            decal.receiveShadows = true;
            decal.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            decal.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            ring.Add(decal);
        }
        else if (pool)
        {
            decal = ring[0];
            ring.RemoveAt(0);
            ring.Add(decal);
            RemoveGrowth(decal.transform);
        }
        else
        {
            s_next %= Decals.Count;
            decal = Decals[s_next++];
            RemoveGrowth(decal.transform);
        }

        decal.GetComponent<MeshFilter>().sharedMesh = mesh;
        decal.sharedMaterial = material;

        // Lifted off the surface by a hair, and a different hair for each of a run of
        // neighbours, so two splats that overlap do not fight over the same depth.
        // Pools sit a hair above the splats, so a body's own pool is not hidden under the
        // spatter it landed in.
        float lift = 0.008f + (ring.IndexOf(decal) % 8) * 0.0015f + (pool ? 0.012f : 0f);

        Vector3 up = normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(along, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Quaternion.AngleAxis(Random.Range(0f, 360f), up) * Vector3.ProjectOnPlane(
                Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward, up);

        var t = decal.transform;
        t.SetPositionAndRotation(point + up * lift, Quaternion.LookRotation(forward.normalized, up));
        t.localScale = new Vector3(size, 1f, size);

        return decal;
    }

    static void RemoveGrowth(Transform target)
    {
        for (int i = Growing.Count - 1; i >= 0; i--)
            if (Growing[i].target == target) Growing.RemoveAt(i);
    }

    /// <summary>Called by <see cref="BloodFXRunner"/>, since a static class has no Update of its own.</summary>
    internal static void Grow()
    {
        for (int i = Growing.Count - 1; i >= 0; i--)
        {
            var g = Growing[i];

            if (g.target == null)
            {
                Growing.RemoveAt(i);
                continue;
            }

            float t = Mathf.Clamp01((Time.time - g.start) / g.duration);

            // Fast, then slowing, the way a liquid actually spreads.
            float eased = 1f - Mathf.Pow(1f - t, g.power > 0f ? g.power : 3f);
            g.target.localScale = Vector3.Lerp(g.from, g.to, eased);

            if (t >= 1f) Growing.RemoveAt(i);
        }
    }

    // ======================================================================
    // Setup
    // ======================================================================

    /// <summary>Builds the level's blood the first time it is needed, and again after a scene change.</summary>
    static bool Ensure(BloodLibrary library)
    {
        if (library == null) library = s_library;
        if (library == null) return false;

        s_library = library;

        if (s_root != null) return true;

        Decals.Clear();
        Pools.Clear();
        Growing.Clear();
        s_next = 0;

        var root = new GameObject("BloodFX");
        root.AddComponent<BloodFXRunner>();
        s_root = root.transform;

        if (library.sprayPrefab != null)
            s_spray = Object.Instantiate(library.sprayPrefab, s_root);

        if (library.mistPrefab != null)
            s_mist = Object.Instantiate(library.mistPrefab, s_root);

        return true;
    }

    /// <summary>One quarter of the 2x2 splat atlas, picked at random.</summary>
    static Mesh Atlas()
    {
        if (s_quads == null || s_quads[0] == null)
        {
            s_quads = new Mesh[4];
            for (int i = 0; i < 4; i++)
                s_quads[i] = Quad(new Vector2((i % 2) * 0.5f, (i / 2) * 0.5f), 0.5f);
        }

        return s_quads[Random.Range(0, 4)];
    }

    static Mesh Whole()
    {
        if (s_whole == null) s_whole = Quad(Vector2.zero, 1f);
        return s_whole;
    }

    /// <summary>A unit quad lying flat, facing up, with the UVs of one tile of a texture.</summary>
    public static Mesh Quad(Vector2 uvMin, float uvSize)
    {
        var mesh = new Mesh { name = "BloodQuad" };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
        };

        mesh.uv = new[]
        {
            uvMin, uvMin + new Vector2(uvSize, 0f),
            uvMin + new Vector2(0f, uvSize), uvMin + new Vector2(uvSize, uvSize)
        };

        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.tangents = new[]
        {
            new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
            new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();

        return mesh;
    }
}
