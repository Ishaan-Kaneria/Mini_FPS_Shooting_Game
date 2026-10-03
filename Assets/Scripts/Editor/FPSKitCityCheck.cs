#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Proves the rooftop zone is one connected place, from the open scene, without leaving the editor
    /// (Tools > MiniFPS > Rooftop > Check Reach). Every walkable roof must have a complete navmesh path from the
    /// player's start, every enemy spawn point must too, and a random sample of the whole bake must be reachable:
    /// anything that is not is an island the spawner could put an enemy on. Towers (roofs with a NavMeshModifier)
    /// are deliberately not walkable and are not counted.
    /// </summary>
    public static class FPSKitCityCheck
    {
        [MenuItem("Tools/MiniFPS/Rooftop/Check Reach")]
        public static void Menu() => Debug.Log("[FPSKit] " + Run());

        public static string Run(int samples = 800)
        {
            var sb = new StringBuilder();
            if (!NavMesh.SamplePosition(Vector3.zero, out var start, 6f, NavMesh.AllAreas)) return "no navmesh at the player start";

            var buildings = GameObject.Find("Arena/Buildings");
            if (buildings == null) return "no Arena/Buildings: open the Night Rooftop scene";

            int walk = 0, reach = 0, towers = 0;
            foreach (Transform b in buildings.transform)
            {
                var roof = b.Find("Roof");
                if (roof == null) continue;
                if (roof.GetComponent<Unity.AI.Navigation.NavMeshModifier>() != null) { towers++; continue; }
                walk++;
                var bounds = roof.GetComponent<MeshRenderer>().bounds;
                var c = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                if (NavMesh.SamplePosition(c, out var h, 14f, NavMesh.AllAreas) && Mathf.Abs(h.position.y - bounds.max.y) < 1f &&
                    Complete(start.position, h.position)) reach++;
                else sb.AppendLine($"  {b.name}: roof not reachable");
            }
            sb.Insert(0, $"walkable roofs {reach}/{walk} reachable ({towers} towers)\n");

            int sp = 0, spOk = 0;
            var spawns = GameObject.Find("SpawnPoints");
            if (spawns != null)
                foreach (Transform p in spawns.transform)
                {
                    sp++;
                    if (NavMesh.SamplePosition(p.position, out var q, 12f, NavMesh.AllAreas) && Complete(start.position, q.position)) spOk++;
                    else sb.AppendLine($"  {p.name}: not reachable");
                }
            sb.AppendLine($"spawn points {spOk}/{sp} reachable");

            var tri = NavMesh.CalculateTriangulation();
            var rng = new System.Random(5);
            int ok = 0, lost = 0;
            for (int i = 0; i < samples; i++)
            {
                int t = rng.Next(0, tri.indices.Length / 3);
                var p = (tri.vertices[tri.indices[t * 3]] + tri.vertices[tri.indices[t * 3 + 1]] + tri.vertices[tri.indices[t * 3 + 2]]) / 3f;
                if (Complete(start.position, p)) ok++; else lost++;
            }
            sb.AppendLine($"navmesh sample {ok}/{samples} reachable ({lost} stranded), {tri.indices.Length / 3} triangles");
            return sb.ToString();
        }

        static bool Complete(Vector3 a, Vector3 b)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
#endif
