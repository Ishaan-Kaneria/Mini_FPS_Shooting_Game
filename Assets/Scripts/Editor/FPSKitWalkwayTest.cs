#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Do the fairground's walkways join up, and do they reach every door?
    ///
    /// <b>It reads the saved scene, not the plan.</b> The builder checks its own plan as it goes,
    /// but a plan that is right and a scene that is right are different claims -- the paving is a
    /// mesh that was wound the wrong way once and was simply not there. So this opens the built scene,
    /// rasterises the paving's triangles into a coarse grid, flood-fills it and counts the pieces; then
    /// it asks, for every entrance marker the builder left, whether there is paving within a few
    /// metres. A missing stone is a gap, not a break: the fill steps across a cell.
    /// </summary>
    public static class FPSKitWalkwayTest
    {
        const float Cell = 2.5f;
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity";

        public static void VerifyWalkways()
        {
            try
            {
                var report = new StringBuilder();
                var problems = new List<string>();

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var arena = GameObject.Find("Arena");
                if (arena == null) { Fail("no Arena in the fairground scene"); return; }

                var cells = new HashSet<Vector2Int>();
                int triangles = 0;

                foreach (var name in new[] { "Walkways", "WalkwaysDark", "WalkwaysLight", "ServiceRoad" })
                {
                    var t = arena.transform.Find(name);
                    if (t == null) continue;

                    var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                    var v = mesh.vertices; var tri = mesh.triangles;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                        cells.Add(new Vector2Int(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell)));
                        triangles++;
                    }
                }

                if (cells.Count < 200) problems.Add($"only {cells.Count} paved cells ({triangles} triangles): the paving is missing or tiny");

                // Flood fill, stepping across up to one empty cell so a missing stone is not a break.
                var seen = new HashSet<Vector2Int>();
                var sizes = new List<int>();
                foreach (var start in cells)
                {
                    if (seen.Contains(start)) continue;

                    int size = 0;
                    var stack = new Stack<Vector2Int>();
                    stack.Push(start); seen.Add(start);

                    while (stack.Count > 0)
                    {
                        var c = stack.Pop(); size++;
                        for (int dx = -2; dx <= 2; dx++)
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                var n = new Vector2Int(c.x + dx, c.y + dz);
                                if (cells.Contains(n) && seen.Add(n)) stack.Push(n);
                            }
                    }

                    sizes.Add(size);
                }

                sizes.Sort(); sizes.Reverse();
                int pieces = 0;
                foreach (var s in sizes) if (s >= 6) pieces++;

                report.Append($"\n  {cells.Count} paved cells in {pieces} piece(s); largest {(sizes.Count > 0 ? sizes[0] : 0)}");
                if (pieces != 1) problems.Add($"the paving is {pieces} separate pieces, not one (sizes {string.Join(", ", sizes.GetRange(0, Mathf.Min(6, sizes.Count)))})");

                // Every entrance stands on paving.
                var entrances = arena.transform.Find("Entrances");
                int checkedCount = 0;
                if (entrances == null) problems.Add("no Entrances group: the builder wrote no door markers");
                else
                    foreach (Transform e in entrances)
                    {
                        checkedCount++;
                        var p = e.position;
                        bool near = false;
                        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                        for (int dx = -2; dx <= 2 && !near; dx++)
                            for (int dz = -2; dz <= 2 && !near; dz++)
                                if (cells.Contains(new Vector2Int(cx + dx, cz + dz))) near = true;

                        if (!near) problems.Add($"{e.name} at ({p.x:0}, {p.z:0}) has no paving within 5m");
                    }

                report.Append($"\n  {checkedCount} entrance(s) checked");

                if (problems.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var pr in problems) sb.Append($"\n  - {pr}");
                    Debug.LogError($"[FPSKitBatch] FAILED: the fairground's walkways do not join up:{sb}{report}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify walkways passed.{report}");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void Fail(string why)
        {
            Debug.LogError($"[FPSKitBatch] FAILED: {why}");
            EditorApplication.Exit(1);
        }
    }
}
#endif
