using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every pane of glass in an arena (Docs/DESERT_GLASS.md), drawn by a small mesh per 64 m chunk and known here only as data: no collider, no
/// object and no update per pane, so thousands cost nothing until one is hit. A shot asks along its line (<see cref="Ray"/>), a blow and a
/// blast likewise. A round cracks ordinary glass and the next breaks it; a kick breaks it at once; a shop's display glass takes three; a
/// tempered door goes whole, in cubes. States only get worse. Broken: its quad in the chunk mesh collapses, jagged teeth stay in the frame, shards
/// fall, it is heard (enemies come, civilians run). Written by FPSKitTownGlass.
/// </summary>
public class GlassField : MonoBehaviour
{
    public enum Kind : byte { Window, Display, Tempered }
    public enum Blow { Round, Heavy, Blast }

    [Serializable]
    public struct Pane
    {
        public Vector3 corner, u, v, normal;
        public Kind kind;
        public int chunk, firstVertex;
    }

    [Tooltip("Written by the builder.")]
    public Pane[] panes;
    [Tooltip("Each chunk's mesh of panes (readable, unbatched): a broken pane's corners are collapsed in it.")]
    public MeshFilter[] chunks;
    [Tooltip("Thin colliders at shop glass, which is at ground level (written by the builder).")]
    public Transform blocks;

    const float Cell = 8f;

    public static GlassField Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    Dictionary<long, List<int>> _cells;
    byte[] _hits;
    int[] _stamp;
    int _visit;
    readonly List<int> _found = new List<int>(8);
    Vector3[][] _verts;

    void Awake()
    {
        Instance = this;
        Index();
        if (blocks != null)
        {
            int bounds = LayerMask.NameToLayer("PlayerBounds");
            foreach (Transform b in blocks) b.gameObject.layer = bounds;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    void Index()
    {
        _cells = new Dictionary<long, List<int>>(panes != null ? panes.Length : 0);
        _hits = new byte[panes != null ? panes.Length : 0];
        _stamp = new int[_hits.Length];
        _verts = new Vector3[chunks != null ? chunks.Length : 0][];
        for (int i = 0; i < _hits.Length; i++)
        {
            ref var p = ref panes[i];
            Vector3 a = p.corner, b = p.corner + p.u;
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell);
            int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = Key(x, z);
                    if (!_cells.TryGetValue(k, out var list)) _cells[k] = list = new List<int>(4);
                    list.Add(i);
                }
        }
    }

    /// <summary>Panes a shot's line crosses, to <paramref name="distance"/>: each takes the blow, and the round goes on.</summary>
    public int Ray(Vector3 from, Vector3 dir, float distance, Blow blow, GameObject source)
    {
        if (panes == null || panes.Length == 0) return 0;
        distance = Mathf.Min(distance + 0.12f, 300f);
        _visit++;
        _found.Clear();
        int lastX = int.MaxValue, lastZ = int.MaxValue;
        for (float t = 0f; ; t += Cell * 0.5f)
        {
            float at = Mathf.Min(t, distance);
            Vector3 q = from + dir * at;
            int cx = Mathf.FloorToInt(q.x / Cell), cz = Mathf.FloorToInt(q.z / Cell);
            if (cx != lastX || cz != lastZ)
            {
                lastX = cx; lastZ = cz;
                // Its neighbours too: half-cell steps can cut a corner of a cell.
                for (int ox = -1; ox <= 1; ox++)
                    for (int oz = -1; oz <= 1; oz++)
                        if (_cells.TryGetValue(Key(cx + ox, cz + oz), out var list))
                            foreach (int i in list) if (_stamp[i] != _visit) { _stamp[i] = _visit; Cross(i, from, dir, distance); }
            }
            if (t >= distance) break;
        }
        int hits = 0;
        foreach (int i in _found)
        {
            Vector3 p = LineOn(i, from, dir);
            Strike(i, p, dir, blow, source);
            if (++hits >= 3) break;
        }
        return hits;
    }

    void Cross(int i, Vector3 from, Vector3 dir, float distance)
    {
        if (_hits[i] == byte.MaxValue) return;
        ref var p = ref panes[i];
        float denom = Vector3.Dot(dir, p.normal);
        if (Mathf.Abs(denom) < 1e-4f) return;
        float t = Vector3.Dot(p.corner - from, p.normal) / denom;
        if (t < 0f || t > distance) return;
        Vector3 at = from + dir * t - p.corner;
        float s = Vector3.Dot(at, p.u) / p.u.sqrMagnitude, w = Vector3.Dot(at, p.v) / p.v.sqrMagnitude;
        if (s >= 0f && s <= 1f && w >= 0f && w <= 1f) _found.Add(i);
    }

    Vector3 LineOn(int i, Vector3 from, Vector3 dir)
    {
        ref var p = ref panes[i];
        return from + dir * (Vector3.Dot(p.corner - from, p.normal) / Vector3.Dot(dir, p.normal));
    }

    /// <summary>Everything within reach of a blast goes, thrown away from it.</summary>
    public void Blast(Vector3 centre, float radius, GameObject source)
    {
        if (panes == null) return;
        _visit++;
        int r = Mathf.CeilToInt(radius / Cell), cx = Mathf.FloorToInt(centre.x / Cell), cz = Mathf.FloorToInt(centre.z / Cell);
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
                if (_cells.TryGetValue(Key(x, z), out var list))
                    foreach (int i in list)
                    {
                        if (_stamp[i] == _visit || _hits[i] == byte.MaxValue) continue;
                        _stamp[i] = _visit;
                        ref var p = ref panes[i];
                        Vector3 mid = p.corner + (p.u + p.v) * 0.5f;
                        Vector3 away = mid - centre;
                        if (away.sqrMagnitude > radius * radius) continue;
                        // Only glass that can see the blast: not the far side of the house.
                        if (Physics.Linecast(centre + Vector3.up * 0.3f, mid - away.normalized * 0.2f, LayerMask.GetMask("Environment"), QueryTriggerInteraction.Ignore)) continue;
                        Strike(i, mid, away.normalized, Blow.Blast, source);
                    }
    }

    void Strike(int i, Vector3 at, Vector3 dir, Blow blow, GameObject source)
    {
        ref var p = ref panes[i];
        int hits = _hits[i];
        bool breaks;
        switch (p.kind)
        {
            case Kind.Tempered: breaks = true; break;
            case Kind.Display: breaks = blow == Blow.Blast || (blow == Blow.Heavy && hits >= 1) || hits >= 2; break;
            default: breaks = blow != Blow.Round || hits >= 1; break;
        }
        if (!breaks)
        {
            _hits[i] = (byte)(hits + 1);
            GlassFX.Crack(at, p.normal, Mathf.Clamp(Mathf.Min(p.u.magnitude, p.v.magnitude) * 0.9f, 0.3f, 1f), false);
            GlassFX.Play(WorldSounds.GlassCrack(), at, 0.8f);
            NoiseSystem.Emit(at, 20f, NoiseKind.Violence, source);
            return;
        }
        Shatter(i, dir, source);
    }

    void Shatter(int i, Vector3 dir, GameObject source)
    {
        ref var p = ref panes[i];
        _hits[i] = byte.MaxValue;
        Collapse(ref p);
        Vector3 mid = p.corner + (p.u + p.v) * 0.5f;
        GlassFX.ClearCracks(mid, Mathf.Max(p.u.magnitude, p.v.magnitude) * 0.6f);
        bool cubes = p.kind == Kind.Tempered;
        if (!cubes) GlassFX.Remains(p.corner, p.u, p.v, p.normal);
        var cam = Camera.main;
        float near = cam != null ? Vector3.Distance(cam.transform.position, mid) : 0f;
        GlassFX.Shards(p.corner, p.u, p.v, dir, cubes, near);
        GlassFX.Play(cubes ? WorldSounds.GlassCubes() : WorldSounds.GlassShatter(p.kind == Kind.Display), mid, 1f);
        NoiseSystem.Emit(mid, 40f, NoiseKind.Violence, source);
    }

    void Collapse(ref Pane p)
    {
        if (p.chunk < 0 || p.chunk >= chunks.Length || chunks[p.chunk] == null) return;
        var mesh = chunks[p.chunk].sharedMesh;
        if (mesh == null || !mesh.isReadable) return;
        var verts = _verts[p.chunk] ??= mesh.vertices;
        for (int k = 1; k < 4; k++) verts[p.firstVertex + k] = verts[p.firstVertex];
        mesh.vertices = verts;
    }
}

/// <summary>One call for every gun and blow: the town's panes and the cars' windows on a line.</summary>
public static class GlassBreak
{
    public static void Ray(Vector3 from, Vector3 dir, float distance, GlassField.Blow blow, GameObject source)
    {
        if (GlassField.Instance != null) GlassField.Instance.Ray(from, dir, distance, blow, source);
        CarGlass.Ray(from, dir, distance, blow);
    }

    public static void Blast(Vector3 centre, float radius, GameObject source)
    {
        if (GlassField.Instance != null) GlassField.Instance.Blast(centre, radius * 1.4f, source);
        CarGlass.Blast(centre, radius * 1.4f);
    }
}
