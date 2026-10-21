#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town's last navigation pass: after the bake, every piece of navmesh that is not joined to the player's
    /// start is put off the bake and the navmesh baked again.
    ///
    /// A kilometre of procedural town makes a great many enclosed places an agent fits into and cannot reach: the hollow
    /// middle of a block whose houses are built to their lot lines, a courtyard behind a shut door, the space under a
    /// ramp, a yard behind its neighbour's wall. Each was its own bug to find (Docs/DESIGN_NOTES.md "The desert is a
    /// town"), and after the findable ones were fixed six per cent of the ground navmesh was still stranded. An enemy put
    /// on one stands there until the clock ends the level; a civilian put on one never leaves it.
    ///
    /// The baked triangles are welded into connected pieces (shared corners, within a centimetre); the piece holding the
    /// player's start is the town, and every other piece is covered by Not Walkable boxes on a one-metre grid: a cell is
    /// covered only where its centre lies on the island's own navmesh, which an agent's erosion keeps half a metre clear of
    /// every wall, so a box never reaches through a wall into the street beyond. Vertically each box holds the island's
    /// own floor (-0.8 to +1.2 m), never a floor above or below it.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static void SealTownIslands(Vector3 playerStart)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null) return;
            if (!NavMesh.SamplePosition(playerStart, out var home, 6f, NavMesh.AllAreas))
            {
                Debug.LogWarning("[FPSKit] town seal: the player's start is not on the navmesh; nothing sealed.");
                return;
            }

            var tri = NavMesh.CalculateTriangulation();
            var verts = tri.vertices; var idx = tri.indices;

            // Weld corners by position: tiles repeat their border vertices.
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = Vector3Int.RoundToInt(verts[i] * 100f);
                if (!weld.TryGetValue(k, out int w)) weld[k] = w = weld.Count;
                id[i] = w;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            for (int t = 0; t < idx.Length; t += 3) { Union(id[idx[t]], id[idx[t + 1]]); Union(id[idx[t]], id[idx[t + 2]]); }

            // The town: the piece under the player's start.
            int town = -1; float best = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float d = (verts[i] - home.position).sqrMagnitude;
                if (d < best) { best = d; town = Find(id[i]); }
            }

            // Island triangles onto a one-metre grid: per cell, the heights of island floor found there.
            var cells = new Dictionary<Vector2Int, List<float>>();
            float islandArea = 0f; var pieces = new HashSet<int>();
            for (int t = 0; t < idx.Length; t += 3)
            {
                int piece = Find(id[idx[t]]);
                if (piece == town) continue;
                Vector3 a = verts[idx[t]], b = verts[idx[t + 1]], c = verts[idx[t + 2]];
                pieces.Add(piece);
                islandArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x));
                int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z)), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z));
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var p = new Vector2(x + 0.5f, z + 0.5f);
                        if (!OnTriangle(p, a, b, c, out float y)) continue;
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out var ys)) cells[key] = ys = new List<float>();
                        if (!ys.Any(v => Mathf.Abs(v - y) < 0.6f)) ys.Add(y);
                    }
            }
            if (cells.Count == 0)
            {
                Debug.Log($"[FPSKit] town seal: no islands ({clock.Elapsed.TotalSeconds:0.0}s).");
                return;
            }

            // Runs along x of cells at one floor height, then runs stacked along z with the same ends into boxes.
            var runs = new List<(int x0, int x1, int z, float y0, float y1)>();
            foreach (var row in cells.SelectMany(kv => kv.Value.Select(h => (cx: kv.Key.x, cz: kv.Key.y, h))).GroupBy(c => c.cz))
            {
                var sorted = row.OrderBy(c => Mathf.Round(c.h * 2f)).ThenBy(c => c.cx).ToList();
                int i = 0;
                while (i < sorted.Count)
                {
                    int s = i; float lo = sorted[i].h, hi = sorted[i].h;
                    while (i + 1 < sorted.Count && sorted[i + 1].cx == sorted[i].cx + 1 && Mathf.Abs(sorted[i + 1].h - sorted[s].h) < 0.6f)
                    { i++; lo = Mathf.Min(lo, sorted[i].h); hi = Mathf.Max(hi, sorted[i].h); }
                    runs.Add((sorted[s].cx, sorted[i].cx, row.Key, lo, hi));
                    i++;
                }
            }
            var boxes = new List<(int x0, int x1, int z0, int z1, float y0, float y1)>();
            foreach (var group in runs.GroupBy(r => (r.x0, r.x1)))
            {
                var list = group.OrderBy(r => r.z).ToList();
                var cur = (list[0].x0, list[0].x1, list[0].z, list[0].z, list[0].y0, list[0].y1);
                for (int k = 1; k < list.Count; k++)
                {
                    var r = list[k];
                    if (r.z == cur.Item4 + 1 && Mathf.Abs(r.y0 - cur.Item5) < 0.6f && Mathf.Abs(r.y1 - cur.Item6) < 0.6f)
                    { cur.Item4 = r.z; cur.Item5 = Mathf.Min(cur.Item5, r.y0); cur.Item6 = Mathf.Max(cur.Item6, r.y1); }
                    else { boxes.Add(cur); cur = (r.x0, r.x1, r.z, r.z, r.y0, r.y1); }
                }
                boxes.Add(cur);
            }

            var holder = new GameObject("IslandSeal").transform;
            holder.SetParent(surface.transform.parent, false);
            foreach (var (bx0, bx1, bz0, bz1, by0, by1) in boxes)
            {
                var go = new GameObject("Seal");
                go.transform.SetParent(holder, false);
                float lo = by0 - 0.8f, hi = by1 + 1.2f;
                go.transform.position = new Vector3((bx0 + bx1 + 1) * 0.5f, (lo + hi) * 0.5f, (bz0 + bz1 + 1) * 0.5f);
                var v = go.AddComponent<NavMeshModifierVolume>();
                v.center = Vector3.zero;   // the component's default is a metre up
                v.size = new Vector3(bx1 - bx0 + 1, hi - lo, bz1 - bz0 + 1);
                v.area = 1;
            }
            surface.BuildNavMesh();
            Debug.Log($"[FPSKit] town seal: {pieces.Count} stranded pieces of navmesh ({islandArea:0} m2) covered by {boxes.Count} volumes " +
                      $"over {cells.Count} cells, navmesh baked again ({clock.Elapsed.TotalSeconds:0.0}s).");
        }

        /// <summary>Whether <paramref name="p"/> (x, z) lies on triangle abc seen from above, and the triangle's height there.</summary>
        private static bool OnTriangle(Vector2 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.y - c.z)) / d;
            float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.y - c.z)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 < 0f || l2 < 0f || l3 < 0f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }
    }
}
#endif
