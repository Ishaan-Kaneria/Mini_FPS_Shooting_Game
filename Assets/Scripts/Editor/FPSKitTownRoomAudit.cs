#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Two questions about the town's house rooms, answered on the saved scenes without rendering (his play-test, 2026-11-03).
    /// Sky: from a grid over each room's floor, casts straight up and at up to 55 degrees off it (front faces only, as a surface draws):
    /// a line that meets nothing sees the sky through the ceiling and the roof. A line that meets a house's inside whose
    /// RoomContents entry would have it switched off with the eye here is the same fault at run time. Furniture: what the
    /// district bakes would have to lightmap, counted before committing a bake to it.
    /// Writes Build/Pilot/room_audit.txt.
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitTownRoomAudit.Batch
    /// </summary>
    public static class FPSKitTownRoomAudit
    {
        const float Grid = 0.5f, Reach = 60f;
        static readonly Vector3[] Lines = BuildLines();

        static Vector3[] BuildLines()
        {
            var lines = new List<Vector3> { Vector3.up };
            foreach (float tilt in new[] { 15f, 35f, 55f })
                for (int i = 0; i < 8; i++)
                    lines.Add(Quaternion.Euler(0f, i * 45f, 0f) * Quaternion.Euler(tilt, 0f, 0f) * Vector3.up);
            return lines.ToArray();
        }

        public static void Batch()
        {
            try
            {
                EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, OpenSceneMode.Single);
                foreach (var district in FPSKitTownRoomScenes.ExistingPaths()) EditorSceneManager.OpenScene(district, OpenSceneMode.Additive);
                Physics.SyncTransforms();
                var sb = new StringBuilder();
                string sky = Sky(sb);
                string furniture = Furniture(sb);
                System.IO.Directory.CreateDirectory("Build/Pilot");
                System.IO.File.WriteAllText("Build/Pilot/room_audit.txt", sb.ToString());
                Debug.Log($"[FPSKit] room audit: {sky}; {furniture}\n" + sb.ToString().Substring(0, Mathf.Min(sb.Length, 5000)));
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] room audit FAILED: " + e); EditorApplication.Exit(1); }
        }

        static string Sky(StringBuilder sb)
        {
            var marks = GameObject.Find("HouseRooms");
            if (marks == null) throw new System.Exception("no HouseRooms marks");
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian", "PlayerBounds");
            var shellRooms = new Dictionary<Renderer, List<Bounds>>();
            foreach (var rc in Object.FindObjectsByType<RoomContents>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var (room, _, shell) in rc.Rooms())
                    foreach (var s in shell)
                        if (s != null) { if (!shellRooms.TryGetValue(s, out var l)) shellRooms[s] = l = new List<Bounds>(); l.Add(room); }

            int houses = 0, rooms = 0, points = 0, skyPoints = 0, roomsWithSky = 0, offShells = 0;
            sb.Append("SKY: lines from a room's floor that meet nothing\n");
            foreach (Transform house in marks.transform)
            {
                houses++;
                foreach (Transform room in house)
                {
                    if (!room.name.StartsWith("Room ")) continue;
                    rooms++;
                    int seen = 0, here = 0;
                    Vector3 first = default;
                    var look = room.Find("Look");
                    Vector3 mid = look != null ? look.position : room.position;
                    for (float x = -6f; x <= 6f; x += Grid)
                        for (float z = -6f; z <= 6f; z += Grid)
                        {
                            var p = mid + new Vector3(x, 0f, z);
                            // Only points really in this house: its own floor just below (a point seen out through a window
                            // stands on the street, with the sky rightly over it), and no wall between them and the room's middle.
                            if (!Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out var floor, 1.2f, mask, QueryTriggerInteraction.Ignore)) continue;
                            if (!floor.collider.name.StartsWith("Interior_")) continue;
                            if (Physics.Linecast(mid, p, mask, QueryTriggerInteraction.Ignore)) continue;
                            points++; here++;
                            var at = floor.point + Vector3.up * 1.6f;
                            var inside = floor.collider.bounds;
                            float ceiling = Physics.Raycast(at, Vector3.up, out var above, Reach, mask, QueryTriggerInteraction.Ignore) ? above.point.y : at.y + 1.2f;
                            bool open = false;
                            foreach (var d in Lines)
                            {
                                if (!Physics.Raycast(at, d, out var over, Reach, mask, QueryTriggerInteraction.Ignore))
                                {
                                    // Out through a window is sky as it should be: it counts only if the line crossed the
                                    // ceiling's height still inside the house.
                                    var cross = at + d * ((ceiling - at.y) / d.y);
                                    if (cross.x > inside.min.x + 0.3f && cross.x < inside.max.x - 0.3f && cross.z > inside.min.z + 0.3f && cross.z < inside.max.z - 0.3f) open = true;
                                    continue;
                                }
                                var r = over.collider.GetComponent<Renderer>();
                                if (r != null && shellRooms.TryGetValue(r, out var boxes) && !boxes.Any(b => b.SqrDistance(at) < 60f * 60f)) offShells++;
                            }
                            if (open) { skyPoints++; if (seen++ == 0) first = at; }
                        }
                    if (seen > 0)
                    {
                        roomsWithSky++;
                        sb.Append($"  {house.name} {room.name}: {seen} of {here} points, first at {first.x:0.0} {first.y:0.0} {first.z:0.0}\n");
                    }
                }
            }
            string line = $"sky from {roomsWithSky} of {rooms} rooms in {houses} houses ({skyPoints} of {points} points); {offShells} lines meet a house inside that would be switched off";
            sb.Append(line).Append('\n');
            return line;
        }

        /// <summary>What lightmapping the rooms' furniture would take: pieces by size, the meshes each would need a copy of, and which have lower LODs.</summary>
        static string Furniture(StringBuilder sb)
        {
            int all = 0, chosen = 0, withLod = 0; long tris = 0, nextTris = 0;
            var unique = new HashSet<Mesh>();
            var names = new Dictionary<string, int>();
            var biggest = new List<(string, int)>();
            foreach (var path in FPSKitTownRoomScenes.ExistingPaths())
            {
                var scene = EditorSceneManager.GetSceneByPath(path);
                var rooms = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Rooms");
                if (rooms == null) continue;
                long districtTris = tris; int districtPieces = chosen;
                foreach (var r in rooms.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.StartsWith("Detail_") || r.name.StartsWith("Interior_") || r.name.StartsWith("ShopInside_") || r.name.StartsWith("ShopWindow_")) continue;
                    all++;
                    var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || !FPSKitTownRoomBake.FurnitureTakesLightmap(r)) continue;
                    chosen++;
                    unique.Add(mesh);
                    tris += mesh.triangles.Length / 3;
                    var group = r.GetComponentInParent<LODGroup>();
                    if (group != null)
                    {
                        withLod++;
                        var lods = group.GetLODs();
                        long own = 0;
                        if (lods.Length > 1)
                            foreach (var o in lods[1].renderers)
                                if (o != null && o.GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh != null) own += f.sharedMesh.triangles.Length / 3;
                        nextTris += own > 0 ? own / lods[0].renderers.Length : mesh.triangles.Length / 3;
                    }
                    else nextTris += mesh.triangles.Length / 3;
                    string n = r.transform.root != r.transform ? r.name : mesh.name;
                    names.TryGetValue(n, out int c); names[n] = c + 1;
                    biggest.Add((mesh.name, mesh.triangles.Length / 3));
                }
                sb.Append($"  {System.IO.Path.GetFileNameWithoutExtension(path)}: {chosen - districtPieces} pieces, {(tris - districtTris) / 1000} k triangles\n");
            }
            string line = $"furniture: {chosen} of {all} pieces would be lightmapped, {unique.Count} meshes, {tris / 1000} k triangles ({nextTris / 1000} k at the next LOD), {withLod} in a LOD group";
            sb.Append("\nFURNITURE: ").Append(line).Append('\n');
            foreach (var kv in names.OrderByDescending(k => k.Value).Take(25)) sb.Append($"  {kv.Value} x {kv.Key}\n");
            sb.Append("largest meshes:\n");
            foreach (var (n, t) in biggest.Distinct().OrderByDescending(b => b.Item2).Take(12)) sb.Append($"  {t} tris {n}\n");
            return line;
        }
    }
}
#endif
