using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The fixed walk through an arena that the realism audit looks along (FPSKitRealismAudit) and the frame-time benchmark
/// walks (WalkProbe), so "before" and "after" are measured on the same ground. Desert Outpost's is about 600 m: from the
/// player's start down the avenue's east pavement, east along West Road, north up East 130, west along the boulevard to the
/// clock square, on into the souk street. Each leg is a navmesh path (short legs: a long route's search gives up).
/// Holds no state.
/// </summary>
public static class WalkRoute
{
    /// <summary>Desert Outpost's waypoints (x, z) after the player's start.</summary>
    public static readonly Vector2[] Desert =
    {
        new Vector2(11f, -124f), new Vector2(60f, -136.5f), new Vector2(124f, -136.5f), new Vector2(124.5f, -60f),
        new Vector2(124.5f, 12f), new Vector2(60f, 11.75f), new Vector2(14f, 4f), new Vector2(0f, -7f),
        new Vector2(-60f, 18f), new Vector2(-130f, 20f),
    };

    /// <summary>The waypoints for an arena's scene, or null where none is set yet.</summary>
    public static Vector2[] For(string sceneName) => sceneName == "DesertOutpost" ? Desert : null;

    /// <summary>
    /// The walk as a polyline on the navmesh, from <paramref name="start"/> through each waypoint (at the start's height,
    /// snapped to the navmesh within 20 m). A leg with no complete path is walked straight, and said so in
    /// <paramref name="notes"/>.
    /// </summary>
    public static List<Vector3> Line(Vector3 start, Vector2[] waypoints, List<string> notes = null)
    {
        var points = new List<Vector3>();
        var stops = new List<Vector3> { start };
        foreach (var w in waypoints) stops.Add(new Vector3(w.x, start.y, w.y));
        Vector3? last = null;
        var path = new NavMeshPath();
        foreach (var w in stops)
        {
            if (!NavMesh.SamplePosition(w, out var hit, 20f, NavMesh.AllAreas)) { notes?.Add($"no navmesh near {w}"); continue; }
            var p = hit.position;
            if (last == null) { points.Add(p); last = p; continue; }
            if (NavMesh.CalculatePath(last.Value, p, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                for (int i = 1; i < path.corners.Length; i++) points.Add(path.corners[i]);
            else { notes?.Add($"no complete path {last.Value} -> {p} ({path.status}); straight"); points.Add(p); }
            last = p;
        }
        return points;
    }

    /// <summary>The polyline's length in metres.</summary>
    public static float Length(List<Vector3> line)
    {
        float l = 0f;
        for (int i = 1; i < line.Count; i++) l += Vector3.Distance(line[i - 1], line[i]);
        return l;
    }
}
