using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A car's windows breaking (Docs/DESERT_GLASS.md, G7). Side and rear windows are tempered: the first hit takes the whole window out in cubes. The
/// windscreen is laminated: rounds star it, and only after three does it give. A merged parked car has one glass surface, so it goes at once.
/// Found by material name on the car's renderers, with one trigger round the cabin so a shot can find it through the open gap of a traffic
/// car's colliders (no other ray notices a trigger). Costs nothing until hit: no update.
/// </summary>
[DisallowMultipleComponent]
public class CarGlass : MonoBehaviour
{
    class Part { public List<Renderer> renderers = new List<Renderer>(); public Bounds bounds; public bool windscreen, gone; public int hits; }

    readonly List<Part> _parts = new List<Part>();
    readonly List<(Renderer r, int slot)> _merged = new List<(Renderer, int)>();
    bool _ready, _mergedGone;

    static readonly HashSet<CarGlass> _all = new HashSet<CarGlass>();
    static RaycastHit[] _hits;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _all.Clear(); _hits = null; }

    void OnEnable() => _all.Add(this);
    void OnDisable() => _all.Remove(this);
    void Start() => Setup();

    static bool IsGlass(Material m)
    {
        if (m == null) return false;
        string n = m.name;
        return n.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Windows", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static string PartName(string n)
    {
        int lod = n.LastIndexOf("_LOD", System.StringComparison.Ordinal);
        return lod > 0 ? n.Substring(0, lod) : n;
    }

    void Setup()
    {
        if (_ready) return;
        _ready = true;
        var byName = new Dictionary<string, Part>();
        bool any = false;
        var cabin = new Bounds();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool whole = mats.Length > 1;
            for (int s = 0; s < mats.Length; s++)
            {
                if (!IsGlass(mats[s])) continue;
                if (!any) { cabin = r.bounds; any = true; } else cabin.Encapsulate(r.bounds);
                if (whole) { _merged.Add((r, s)); continue; }
                string name = PartName(r.name);
                if (!byName.TryGetValue(name, out var part))
                {
                    part = new Part { bounds = r.bounds, windscreen = name.IndexOf("Front", System.StringComparison.Ordinal) >= 0 && name.IndexOf("Glass", System.StringComparison.Ordinal) >= 0 };
                    byName[name] = part;
                    _parts.Add(part);
                }
                part.renderers.Add(r);
                part.bounds.Encapsulate(r.bounds);
            }
        }
        if (!any) return;
        // The cabin, in the car's own space: rounds can come in between a traffic car's colliders.
        var box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        Vector3 c = transform.InverseTransformPoint(cabin.center);
        var s2 = cabin.size;
        float wide = Mathf.Abs(Vector3.Dot(transform.right, Vector3.right));
        box.center = c;
        box.size = new Vector3(Mathf.Lerp(s2.z, s2.x, wide) + 0.04f, s2.y + 0.04f, Mathf.Lerp(s2.x, s2.z, wide) + 0.04f);
    }

    /// <summary>The cars' windows a shot's line goes through.</summary>
    public static void Ray(Vector3 from, Vector3 dir, float distance, GlassField.Blow blow)
    {
        if (_all.Count == 0) return;
        _hits ??= new RaycastHit[8];
        int n = Physics.RaycastNonAlloc(from, dir, _hits, distance + 0.1f, LayerMask.GetMask("Vehicle"), QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            if (!_hits[i].collider.isTrigger) continue;
            var car = _hits[i].collider.GetComponent<CarGlass>();
            if (car != null) car.Struck(_hits[i].point, dir, blow);
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
            foreach (var p in car._parts) car.Break(p, away);
            car.BreakMerged(away);
        }
    }

    public void Struck(Vector3 at, Vector3 dir, GlassField.Blow blow)
    {
        Setup();
        if (_merged.Count > 0) { BreakMerged(dir); return; }
        Part best = null; float bestD = float.MaxValue;
        foreach (var p in _parts)
        {
            if (p.gone) continue;
            float d = p.bounds.SqrDistance(at);
            if (d < bestD) { bestD = d; best = p; }
        }
        if (best == null || bestD > 0.5f) return;
        if (best.windscreen && blow != GlassField.Blow.Blast && ++best.hits < 3)
        {
            GlassFX.Crack(at, -dir, 0.55f, true);
            GlassFX.Play(WorldSounds.GlassCrack(), at, 0.9f);
            NoiseSystem.Emit(at, 20f, NoiseKind.Violence, gameObject);
            return;
        }
        Break(best, dir);
    }

    void Break(Part p, Vector3 dir)
    {
        if (p.gone) return;
        p.gone = true;
        foreach (var r in p.renderers) if (r != null) r.enabled = false;
        Burst(p.bounds, dir);
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
        Burst(bounds, dir);
    }

    static void Burst(Bounds b, Vector3 dir)
    {
        Vector3 corner = b.min + Vector3.up * b.size.y * 0.45f;
        GlassFX.Shards(corner, new Vector3(b.size.x, 0f, 0f), new Vector3(0f, b.size.y * 0.4f, b.size.z), dir, true, 0f);
        GlassFX.Play(WorldSounds.GlassCubes(), b.center, 1f);
        NoiseSystem.Emit(b.center, 40f, NoiseKind.Violence, null);
    }
}
