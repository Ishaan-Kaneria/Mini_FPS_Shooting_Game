#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert's rooms in scenes of their own, a district each (his pick, 2026-11-03: "split the town into scenes"). Real
    /// indoor light here is lightmaps (the pilot's rooms measure 2-6 % of the open ground's light by their openings and
    /// 0.5-1.8 % at the back, as real rooms; the 3 m probes leak the light over the roofs or hold none), and one bake of every
    /// room in the town does not fit this laptop's 14.5 GB (one district at 20 cm peaked at 11 GB). Unity bakes a scene's
    /// lightmaps with that scene, so each district's rooms, baked on their own with the town loaded for its shade and bounce,
    /// fit.
    ///
    /// Run on the saved desert (after its build and bakes): the town is cut into <see cref="Columns"/> x <see cref="Rows"/>
    /// districts; every house's and shop's inside (HouseInteriors, ShopInteriors, their Detail_ meshes too) and every piece of
    /// furniture standing in a room (RoomContents' lists) goes to DesertOutpost_Rooms_k by where it is, each with a
    /// RoomContents of its own (a scene cannot reference another's renderers). The navmesh, baked before, stays in the town's
    /// scene and holds while the rooms are loaded with it. Loading them with the level, and baking them, are the next steps.
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitTownRoomScenes.Batch
    /// </summary>
    public static class FPSKitTownRoomScenes
    {
        public const int Columns = 3, Rows = 2;
        const float Span = 1060f;       // FPSKitTownGI.TownSpan: the town and a little over, centred on the origin

        public static string ScenePath(int district) => $"{System.IO.Path.GetDirectoryName(FPSKitTownGI.DesertScene)}/DesertOutpost_Rooms_{district + 1}.unity";
        public static int Count => Columns * Rows;

        /// <summary>Which district a point is in (0 .. Count-1): columns west to east, rows south to north.</summary>
        public static int DistrictOf(Vector3 p)
        {
            int c = Mathf.Clamp(Mathf.FloorToInt((p.x + Span * 0.5f) / (Span / Columns)), 0, Columns - 1);
            int r = Mathf.Clamp(Mathf.FloorToInt((p.z + Span * 0.5f) / (Span / Rows)), 0, Rows - 1);
            return c + r * Columns;
        }

        public static void Batch() => Run(save: true);

        /// <summary>The split done in memory and reported, nothing saved (the town on disk is untouched).</summary>
        public static void BatchDry() => Run(save: false);

        static void Run(bool save)
        {
            try
            {
                var report = Split(FPSKitTownGI.DesertScene, save);
                Debug.Log("[FPSKit] room scenes: " + report);
                EditorApplication.Exit(report.StartsWith("FAILED") ? 1 : 0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] room scenes FAILED: " + e); EditorApplication.Exit(1); }
        }

        /// <summary>The district scenes on disk, in order (empty before the first split).</summary>
        public static List<string> ExistingPaths()
            => Enumerable.Range(0, Count).Select(ScenePath).Where(System.IO.File.Exists).ToList();

        /// <summary>The district scenes in Build Settings, right after the town (RoomScenes loads them by name).</summary>
        public static void Register(string townPath)
        {
            var list = EditorBuildSettings.scenes.Where(s => !ExistingPaths().Contains(s.path)).ToList();
            int at = list.FindIndex(s => s.path == townPath);
            var rooms = ExistingPaths().Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            if (at < 0) list.AddRange(rooms); else list.InsertRange(at + 1, rooms);
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>Splits the saved town at <paramref name="townPath"/>; saves it and the district scenes. Returns a report.</summary>
        public static string Split(string townPath, bool save = true)
        {
            var town = EditorSceneManager.OpenScene(townPath, OpenSceneMode.Single);
            var arena = GameObject.Find("Arena");
            if (arena == null) return "FAILED: no Arena";
            var contents = Object.FindAnyObjectByType<RoomContents>(FindObjectsInactive.Include);

            // What goes where: top objects (a whole inside, a whole prop), by district.
            var moves = new List<GameObject>[Count];
            for (int i = 0; i < Count; i++) moves[i] = new List<GameObject>();
            var taken = new HashSet<GameObject>();
            void Take(GameObject top, Vector3 at)
            {
                if (top == null || !taken.Add(top)) return;
                moves[DistrictOf(at)].Add(top);
            }
            foreach (var group in new[] { "Buildings/HouseInteriors", "Buildings/ShopInteriors" })
                if (arena.transform.Find(group) is Transform g)
                    foreach (Transform inside in g.Cast<Transform>().ToList())
                    {
                        var r = inside.GetComponentInChildren<Renderer>(true);
                        Take(inside.gameObject, r != null ? r.bounds.center : inside.position);
                    }

            // The rooms, for each district's RoomContents, and their furniture.
            var rooms = new List<(Bounds room, Renderer[] contents, Renderer[] shell)>[Count];
            for (int i = 0; i < Count; i++) rooms[i] = new List<(Bounds, Renderer[], Renderer[])>();
            var props = arena.transform.Find("Buildings/BuildingProps");
            if (contents != null)
                foreach (var room in contents.Rooms())
                {
                    rooms[DistrictOf(room.room.center)].Add(room);
                    foreach (var r in room.contents)
                    {
                        if (r == null) continue;
                        // The piece's own top object under BuildingProps (a prop is a hierarchy; move it whole).
                        var t = r.transform;
                        while (t.parent != null && t.parent != props) t = t.parent;
                        if (t.parent == props) Take(t.gameObject, t.position);
                    }
                }

            // The district scenes: new, the objects moved in as roots (a scene's roots only), a RoomContents each.
            var lines = new List<string>();
            for (int k = 0; k < Count; k++)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject("Rooms").transform;
                SceneManager.MoveGameObjectToScene(root.gameObject, scene);
                int renderers = 0;
                foreach (var go in moves[k])
                {
                    go.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetParent(root, true);
                    renderers += go.GetComponentsInChildren<Renderer>(true).Length;
                }
                if (contents != null && rooms[k].Count > 0)
                {
                    var rc = new GameObject("RoomContents").AddComponent<RoomContents>();
                    rc.transform.SetParent(root, false);
                    rc.radius = contents.radius; rc.shellRadius = contents.shellRadius; rc.interval = contents.interval;
                    var first = new List<int>(); var list = new List<Renderer>(); var shellFirst = new List<int>(); var shells = new List<Renderer>();
                    foreach (var (box, inRoom, shell) in rooms[k])
                    {
                        first.Add(list.Count); list.AddRange(inRoom.Where(r => r != null && r.gameObject.scene == scene));
                        shellFirst.Add(shells.Count); shells.AddRange(shell.Where(r => r != null && r.gameObject.scene == scene));
                    }
                    rc.Set(rooms[k].Select(r => r.room).ToArray(), first.ToArray(), list.ToArray(), shellFirst.ToArray(), shells.ToArray());
                }
                EditorSceneManager.SaveScene(scene, ScenePath(k));     // always: Unity makes no second new scene while one is untitled (nothing loads them until the level does)
                lines.Add($"{k + 1}: {moves[k].Count} objects, {renderers} renderers, {rooms[k].Count} rooms");
            }
            // The town keeps no RoomContents: every room's contents and inside are in a district now. It loads its districts.
            if (contents != null) Object.DestroyImmediate(contents.gameObject);
            var loader = arena.GetComponentInChildren<RoomScenes>(true) ?? new GameObject("RoomScenes").AddComponent<RoomScenes>();
            loader.transform.SetParent(arena.transform, false);
            loader.sceneNames = Enumerable.Range(0, Count).Select(k => System.IO.Path.GetFileNameWithoutExtension(ScenePath(k))).ToArray();
            if (save) Register(townPath);
            if (save) { EditorSceneManager.MarkSceneDirty(town); EditorSceneManager.SaveScene(town, townPath); }
            int left = arena.transform.Find("Buildings/HouseInteriors")?.childCount ?? 0;
            return $"{(save ? "" : "(dry run, nothing saved) ")}{taken.Count} objects moved into {Count} districts ({string.Join("; ", lines)}); left in the town's HouseInteriors: {left}";
        }
    }
}
#endif
