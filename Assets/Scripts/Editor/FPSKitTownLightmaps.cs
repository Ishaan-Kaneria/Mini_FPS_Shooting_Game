#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Baked light inside the town: the interior lighting pilot (Part B, 2026-10-31). The rooms were lit by probes 3 m
    /// apart (FPSKitTownGI), which cannot draw what his screenshots missed: a wall blown white beside a black room, a hard
    /// bright band across the majlis, nothing darker in a corner or under a shelf, a lamp glowing over a room it does not
    /// light. A lightmap can: the light through the door and the windows bounced round the room at 5 cm, ambient
    /// occlusion in every corner, and a shop's tubes lighting its shelves.
    ///
    /// The pilot is the two old houses' first rooms and the lit shop nearest the player's start (ViewProbe.PilotViews):
    ///   * their insides (the whole house's Interior_n, the shop block's ShopInside_x_y) get lightmap UVs and take their
    ///     indirect light from a lightmap; the sun stays real-time (Mixed, Baked Indirect), so their direct light and
    ///     shadows are as before and only what it bounces is baked;
    ///   * the furniture in them shades the bake (Contribute GI) but stays on the probes (no lightmap of its own);
    ///   * by day the houses' lamps are off (FPSKitTownInterior); a trading shop's tubes are on, as baked lights over the
    ///     fittings (FPSKitTownShopfront.ShopTubes), so they cost nothing to draw.
    /// Run inside the classic probe bake (FPSKitTownProbes.BakeTownProbes), at the adaptive probe volumes' strength; the
    /// people's probes from the same bake are then scaled down to their own level. The adaptive probe volume bake after it leaves anything with a
    /// lightmap on it (FPSKitTownGI).
    /// </summary>
    public static class FPSKitTownLightmaps
    {
        /// <summary>Lightmap texels a metre inside what can be walked into (houses, open-fronted shops): 10 cm, his pick
        /// (2026-10-31) of the pilot's measured choices; corners and the ground under furniture read. The pilot began at 5 cm.</summary>
        public const float TexelsPerMetre = 10f;

        /// <summary>The share of that for insides only seen through glass or under a shutter (ShopWindow_*): 20 cm.</summary>
        const float SeenOnlyScale = 0.5f;

        /// <summary>A shop tube's brightness as a baked area light (a 6 cm by 1.2 m strip under the tube). At 8 the tubes showed
        /// and lit almost nothing: the back wall came out darker than the flat paint it replaced (2026-10-31).</summary>
        const float TubeIntensity = 20f;

        /// <summary>A cool white, as the tubes in the reference shops (Build/Realism/reference_interiors/shop_01-03).</summary>
        static readonly Color TubeColour = new Color(0.94f, 0.97f, 1f);

        public sealed class Plan
        {
            public readonly List<MeshRenderer> lit = new List<MeshRenderer>();
            public int shading, tubes;
            public float area;
        }

        /// <summary>
        /// Chooses the pilot's spaces in the open (saved) desert scene and readies them for the bake: lightmap UVs, Receive GI
        /// Lightmaps, the furniture in them Contribute GI, the tubes as baked lights, and the marks the views use. Returns
        /// null (and bakes nothing new) when there is no player or nothing to light. Idempotent: a re-bake replaces its lights.
        /// </summary>
        public static Plan Prepare(LightingSettings settings)
        {
            var old = GameObject.Find("LightmapPilot");
            if (old != null) Object.DestroyImmediate(old);
            var player = GameObject.FindWithTag("Player");
            var arena = GameObject.Find("Arena");
            if (player == null || arena == null) return null;

            var views = ViewProbe.PilotViews(player.transform.position);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian");
            var root = new GameObject("LightmapPilot").transform;
            root.SetParent(arena.transform, false);
            var plan = new Plan();
            var houses = GameObject.Find("Arena/Buildings/HouseInteriors");
            var shops = GameObject.Find("Arena/Buildings/ShopInteriors");
            foreach (var (name, pos, target) in views)
            {
                bool shop = name == "shop";
                var group = shop ? shops : houses;
                // The inside the view's room is in: the smallest whose bounds hold it (the shop's middle, the room's mark).
                var inside = group == null ? null : group.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => !r.name.StartsWith("Detail_") && r.bounds.Contains(shop ? target : pos))
                    .OrderBy(r => r.bounds.size.x * r.bounds.size.y * r.bounds.size.z).FirstOrDefault();
                if (inside == null) { Debug.LogWarning($"[FPSKit] lightmap pilot: no inside found for {name} at {pos}"); continue; }

                Mark(root, name, pos, target);
                // A room's second view, facing its light (from its way in, else its middle).
                if (!shop)
                {
                    var shape = FindRoom(pos, toSun, mask);
                    if (shape.cells.Count * RoomShape.Cell * RoomShape.Cell < 4f) shape = FindRoom(target, toSun, mask);
                    if (FacingTheLight(shape) is var (feet, toward)) Mark(root, name + "_openings", feet, toward);
                }
                if (plan.lit.Contains(inside)) continue;
                plan.lit.Add(inside);
                plan.area += Lightmapped(inside);

                // The furniture in it shades the bake, lit itself by the probes as before.
                var space = inside.bounds;
                foreach (var r in arena.transform.Find("Buildings/BuildingProps")?.GetComponentsInChildren<MeshRenderer>(true) ?? new MeshRenderer[0])
                {
                    if (!r.enabled || !space.Contains(r.bounds.center)) continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
                    r.receiveGI = ReceiveGI.LightProbes;
                    plan.shading++;
                }
                // A shop block's trading shops: their tubes, lit.
                if (shop && GameObject.Find("ShopRooms") is GameObject marks)
                    foreach (Transform s in marks.transform)
                    {
                        if (s.name.EndsWith(" dark") || !s.name.StartsWith("Shop ") || !space.Contains(s.position)) continue;
                        foreach (var (at, along) in FPSKitSceneBuilder.ShopTubes(s.position, s.right, s.forward, s.localScale.x, s.localScale.y))
                            Tube(root, at, along);
                        plan.tubes++;
                    }
            }
            if (plan.lit.Count == 0) { Object.DestroyImmediate(root.gameObject); return null; }

            settings.lightmapResolution = TexelsPerMetre;
            settings.lightmapPadding = 3;
            settings.lightmapMaxSize = 1024;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;   // the walls' normal maps answer the bounce's direction
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.aoExponentIndirect = 1f;
            settings.aoExponentDirect = 0f;      // the sun is real-time: its shadows are its own
            return plan;
        }

        static void Mark(Transform root, string name, Vector3 feet, Vector3 look)
        {
            var mark = new GameObject(name).transform;
            mark.SetParent(root, false);
            mark.position = feet;
            var to = new GameObject("Look").transform;
            to.SetParent(mark, false);
            to.position = look;
        }

        /// <summary>The inside takes a lightmap: lightmap UVs made for it (its surface area back, for the report).</summary>
        static float Lightmapped(MeshRenderer r)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            float area = 0f;
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            if (mesh.uv2 == null || mesh.uv2.Length == 0)
            {
                UnwrapParam.SetDefaults(out var p);
                // Two texels between islands at the size the inside will have in its lightmap.
                p.packMargin = Mathf.Clamp(2f / (Mathf.Sqrt(area) * TexelsPerMetre), 0.001f, 0.03f);
                Unwrapping.GenerateSecondaryUVSet(mesh, p);
            }
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
            r.receiveGI = ReceiveGI.Lightmaps;
            r.scaleInLightmap = r.name.StartsWith("ShopWindow") ? SeenOnlyScale : 1f;
            return area;
        }

        /// <summary>A tube's light: a strip as long as the tube, just under it, facing down, baked only.</summary>
        static void Tube(Transform root, Vector3 at, Vector3 along)
        {
            var go = new GameObject("ShopTube");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at - Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.down, along));
            var l = go.AddComponent<Light>();
            l.type = LightType.Rectangle;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.areaSize = new Vector2(0.06f, 1.2f);     // across, along the tube
            l.color = TubeColour;
            l.intensity = TubeIntensity;
            l.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// Batch (UNITY_GRAPHICS=1, -force-vulkan): where the light in each of the pilot's rooms comes from, on the saved
        /// scene. For each LightmapPilot view: the room found by casting from its middle; on a 0.5 m floor grid, which points
        /// the sun reaches (its patch) and the baked light (the lightmap, read back through the GPU) against the distance
        /// to the nearest opening (door or window: a cast from the floor out through it to the sky), plus the ceiling and
        /// the walls; a map of the floor, one character a point. Writes Build/Pilot/light_report.txt.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitTownLightmaps.BatchLightReport -force-vulkan
        /// </summary>
        public static void BatchLightReport()
        {
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
                var text = LightReport();
                System.IO.Directory.CreateDirectory("Build/Pilot");
                System.IO.File.WriteAllText("Build/Pilot/light_report.txt", text);
                Debug.Log("[FPSKit] light report:\n" + text);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] light report FAILED: " + e); EditorApplication.Exit(1); }
        }

        static string LightReport()
        {
            var sb = new System.Text.StringBuilder();
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            sb.AppendLine($"sun: elevation {Mathf.Asin(toSun.y) * Mathf.Rad2Deg:0.0}, from azimuth {Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg:0} (0 north, 90 east)");
            // The lightmaps, readable: blitted to half floats and read back.
            var maps = LightmapSettings.lightmaps.Select(m =>
            {
                var tex = m.lightmapColor;
                if (tex == null) return null;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, rt);
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBAHalf, false, true);
                var was = RenderTexture.active;
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
                return copy;
            }).ToArray();
            float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            // The baked light where a cast lands, or -1 when it lands on something with no lightmap.
            float Baked(RaycastHit hit)
            {
                var r = hit.collider.GetComponent<MeshRenderer>();
                if (r == null || r.lightmapIndex < 0 || r.lightmapIndex >= maps.Length || maps[r.lightmapIndex] == null) return -1f;
                var uv = hit.lightmapCoord;
                return Lum(maps[r.lightmapIndex].GetPixelBilinear(uv.x, uv.y));
            }
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian");
            var pilot = GameObject.Find("LightmapPilot");
            if (pilot == null) return sb.AppendLine("no LightmapPilot in the scene").ToString();
            foreach (Transform view in pilot.transform)
            {
                var look = view.Find("Look");
                // The rooms (each from its way in) and the shop; not the second views facing the light.
                if (look != null && !view.name.EndsWith("_openings")) RoomReport(sb, view.name, view.name == "shop" ? look.position : view.position, toSun, mask, Baked);
            }
            return sb.ToString();
        }
        /// <summary>A room as the flood fill finds it: its floor cells (25 cm), floor and ceiling heights, and its openings.</summary>
        public sealed class RoomShape
        {
            public const float Cell = 0.25f;
            public readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            public float floorY, ceilY;
            /// <summary>Edges of the floor where a level cast goes on through the wall: where, which way out, out to the open
            /// air or into another room, and whether the sun comes in there.</summary>
            public readonly List<(Vector3 at, Vector3 dir, bool outside, bool sun)> edges = new List<(Vector3, Vector3, bool, bool)>();
            public Vector3 World(Vector2Int c, float y) => new Vector3(c.x * Cell, y, c.y * Cell);
            public Vector3 Centroid => cells.Count == 0 ? Vector3.zero : cells.Aggregate(Vector3.zero, (acc, c) => acc + World(c, floorY)) / cells.Count;
            /// <summary>The edges grouped into openings (same way out, within 0.6 m of each other), widest first.</summary>
            public List<List<(Vector3 at, Vector3 dir, bool outside, bool sun)>> Openings()
            {
                var groups = new List<List<(Vector3 at, Vector3 dir, bool outside, bool sun)>>();
                foreach (var e in edges)
                {
                    var g = groups.FirstOrDefault(gr => gr[0].dir == e.dir && gr.Any(x => (x.at - e.at).sqrMagnitude < 0.36f));
                    if (g == null) groups.Add(new List<(Vector3, Vector3, bool, bool)> { e }); else g.Add(e);
                }
                return groups.OrderByDescending(g => g.Count).ToList();
            }
        }

        /// <summary>
        /// The room round <paramref name="at"/> (a point on its floor): a 25 cm flood fill held in by the walls at 1.2 m and,
        /// just under the ceiling, by the heads over its doorways, and by needing a ceiling over it; then its openings, a
        /// level cast at 1.2 m from each edge cell that goes on more than 0.6 m (through the wall), out to the open air when
        /// 4 m on there is no roof overhead, the sun coming in when a cast from the cell towards it, at 0.3-2 m, is clear.
        /// </summary>
        public static RoomShape FindRoom(Vector3 at, Vector3 toSun, int mask)
        {
            var shape = new RoomShape();
            const float Cell = RoomShape.Cell;
            shape.floorY = Physics.Raycast(at + Vector3.up * 1.0f, Vector3.down, out var fh, 5f, mask, QueryTriggerInteraction.Ignore) ? fh.point.y : at.y;
            // The ceiling: the highest of five casts up round the point, so a fan or a lamp over the middle is not taken for it.
            float ceil = shape.floorY + 2.2f;
            foreach (var o in new[] { Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                if (Physics.Raycast(new Vector3(at.x, shape.floorY + 1.2f, at.z) + o * 0.9f, Vector3.up, out var ch, 6f, mask, QueryTriggerInteraction.Ignore))
                    ceil = Mathf.Max(ceil, ch.point.y);
            shape.ceilY = ceil;
            float headY = Mathf.Max(shape.floorY + 1.4f, ceil - 0.1f);
            var start = new Vector2Int(Mathf.RoundToInt(at.x / Cell), Mathf.RoundToInt(at.z / Cell));
            var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            shape.cells.Add(start);
            var queue = new Queue<Vector2Int>(shape.cells);
            while (queue.Count > 0 && shape.cells.Count < 2400)
            {
                var c = queue.Dequeue();
                foreach (var d in steps)
                {
                    var n = c + d;
                    if (shape.cells.Contains(n)) continue;
                    if (Physics.Linecast(shape.World(c, shape.floorY + 1.2f), shape.World(n, shape.floorY + 1.2f), mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(shape.World(c, headY), shape.World(n, headY), mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Raycast(shape.World(n, shape.floorY + 1.2f), Vector3.up, out var up, 6f, mask, QueryTriggerInteraction.Ignore) || up.point.y > ceil + 0.3f) continue;
                    shape.cells.Add(n);
                    queue.Enqueue(n);
                }
            }
            foreach (var c in shape.cells)
                foreach (var d in steps)
                {
                    if (shape.cells.Contains(c + d)) continue;
                    var dir = new Vector3(d.x, 0f, d.y);
                    var o = shape.World(c, shape.floorY + 1.2f);
                    float reach = Physics.Raycast(o, dir, out var hit, 8f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : 8f;
                    if (reach < 0.6f) continue;
                    var past = o + dir * Mathf.Min(reach, 4f) - dir * 0.05f;
                    bool outside = !Physics.Raycast(past, Vector3.up, 40f, mask, QueryTriggerInteraction.Ignore);
                    bool sun = false;
                    foreach (float h in new[] { 0.3f, 0.9f, 1.5f, 2.0f })
                        if (!Physics.Raycast(shape.World(c, shape.floorY + h), toSun, 300f, mask, QueryTriggerInteraction.Ignore)) sun = true;
                    shape.edges.Add((shape.World(c, shape.floorY), dir, outside, sun));
                }
            return shape;
        }

        /// <summary>
        /// A view of the room facing its light: from the far side of the floor from its main opening to the open air (one the
        /// sun comes in at, else the widest), a little in from the walls, looking across the room towards it, so the opening,
        /// the floor it lights and the fall-off towards the far corners are all in the frame. Null when it has none.
        /// </summary>
        public static (Vector3 feet, Vector3 look)? FacingTheLight(RoomShape room)
        {
            var main = room.Openings().Where(g => g.Any(e => e.outside)).OrderByDescending(g => g.Any(e => e.sun)).ThenByDescending(g => g.Count).FirstOrDefault();
            if (main == null || room.cells.Count == 0) return null;
            var opening = main.Aggregate(Vector3.zero, (acc, e) => acc + e.at) / main.Count;
            var centre = room.Centroid;
            var far = room.cells.Select(c => room.World(c, room.floorY)).OrderByDescending(p => (p - opening).sqrMagnitude).First();
            var feet = Vector3.MoveTowards(far, centre, 0.7f);
            var look = Vector3.Lerp(opening, centre, 0.35f) + Vector3.up * 1.1f;
            return (feet, look);
        }

        /// <summary>
        /// One room's light (BatchLightReport): its openings, the sun on its floor and the baked light by distance from the
        /// openings to the open air, the ceiling over and away from the sun's patch, the walls near and away from the
        /// openings, and a map of the floor.
        /// </summary>
        static void RoomReport(System.Text.StringBuilder sb, string name, Vector3 at, Vector3 toSun, int mask, System.Func<RaycastHit, float> baked)
        {
            const float Cell = RoomShape.Cell;
            var shape = FindRoom(at, toSun, mask);
            var room = shape.cells;
            float floorY = shape.floorY, ceilY = shape.ceilY;
            Vector3 W(Vector2Int c, float y) => shape.World(c, y);
            var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            var openings = shape.edges;
            var groups = shape.Openings();
            string Way(Vector3 d) => d.z > 0.5f ? "north" : d.z < -0.5f ? "south" : d.x > 0.5f ? "east" : "west";
            sb.AppendLine($"\n== {name}: {room.Count * Cell * Cell:0.0} m² of floor, ceiling {ceilY - floorY:0.00} m");
            foreach (var g in groups)
            {
                var mid = g.Aggregate(Vector3.zero, (acc, x) => acc + x.at) / g.Count;
                sb.AppendLine($"  opening {Way(g[0].dir)}, {g.Count * Cell:0.00} m wide at ({mid.x - at.x:0.0}, {mid.z - at.z:0.0}): " +
                              $"{(g.Any(x => x.outside) ? "to the open air" : "to another room")}, sun {(g.Any(x => x.sun) ? "comes in" : "does not come in")}");
            }
            var outsideAt = openings.Where(e => e.outside).Select(e => new Vector2(e.at.x, e.at.z)).ToList();
            int sunlit = 0;
            var cells = new Dictionary<Vector2Int, (bool lit, float baked)>();
            var bands = new List<(float dist, float baked)>();
            foreach (var c in room)
            {
                var top = W(c, floorY + 0.6f);
                if (!Physics.Raycast(top, Vector3.down, out var hit, 1.2f, mask, QueryTriggerInteraction.Ignore)) continue;
                bool lit = !Physics.Raycast(hit.point + Vector3.up * 0.03f, toSun, 300f, mask, QueryTriggerInteraction.Ignore);
                float b = baked(hit);
                cells[c] = (lit, b);
                if (lit) sunlit++;
                if (b >= 0f && outsideAt.Count > 0) bands.Add((outsideAt.Min(o => Vector2.Distance(o, new Vector2(hit.point.x, hit.point.z))), b));
            }
            sb.AppendLine($"  floor in direct sun: {sunlit} of {cells.Count} points ({100f * sunlit / Mathf.Max(1, cells.Count):0}%)");
            var vals = cells.Values.Where(v => v.baked >= 0f).Select(v => v.baked).OrderBy(v => v).ToList();
            if (vals.Count > 0) sb.AppendLine($"  baked light on the floor (linear): min {vals[0]:0.000}, median {vals[vals.Count / 2]:0.000}, max {vals[vals.Count - 1]:0.000}, {vals.Count} of {cells.Count} points lightmapped");
            foreach (var (from, to) in new[] { (0f, 1f), (1f, 2f), (2f, 3f), (3f, 4f), (4f, 6f), (6f, 99f) })
            {
                var band = bands.Where(x => x.dist >= from && x.dist < to).Select(x => x.baked).ToList();
                if (band.Count > 0) sb.AppendLine($"    {from:0}-{(to > 50 ? "" : to.ToString("0"))} m from an opening to the open air: mean {band.Average():0.000} ({band.Count})");
            }
            // The ceiling over the sun's patch (within 2 m of it) and away from it; the walls near the openings and away.
            var patch = cells.Where(kv => kv.Value.lit).Select(kv => kv.Key).ToList();
            var near = new List<float>(); var far = new List<float>();
            foreach (var c in room)
                if (Physics.Raycast(W(c, floorY + 1.2f), Vector3.up, out var hit, 4f, mask, QueryTriggerInteraction.Ignore) && baked(hit) is float v && v >= 0f)
                    (patch.Any(p => (p - c).sqrMagnitude * Cell * Cell < 4f) ? near : far).Add(v);
            if (near.Count + far.Count > 0)
                sb.AppendLine($"  ceiling: over the sun's patch {(near.Count > 0 ? near.Average().ToString("0.000") : "-")}, away from it {(far.Count > 0 ? far.Average().ToString("0.000") : "-")}");
            var wallNear = new List<float>(); var wallFar = new List<float>();
            foreach (var c in room)
                foreach (var d in steps)
                {
                    if (room.Contains(c + d)) continue;
                    var dir = new Vector3(d.x, 0f, d.y);
                    if (!Physics.Raycast(W(c, floorY + 1.5f), dir, out var hit, 0.6f, mask, QueryTriggerInteraction.Ignore) || !(baked(hit) is float v) || v < 0f) continue;
                    float dist = outsideAt.Count == 0 ? 99f : outsideAt.Min(o => Vector2.Distance(o, new Vector2(hit.point.x, hit.point.z)));
                    (dist < 2f ? wallNear : wallFar).Add(v);
                }
            if (wallNear.Count + wallFar.Count > 0)
                sb.AppendLine($"  walls at 1.5 m: within 2 m of an opening {(wallNear.Count > 0 ? wallNear.Average().ToString("0.000") : "-")}, further {(wallFar.Count > 0 ? wallFar.Average().ToString("0.000") : "-")}");
            // The map: north up, # sun, the baked light in fifths of the room's range (. dark to + bright), ? not lightmapped.
            int x0 = room.Min(c => c.x), x1 = room.Max(c => c.x), z0 = room.Min(c => c.y), z1 = room.Max(c => c.y);
            const string shades = ".:-=+";
            sb.AppendLine("  floor (north up; # sun on it; . : - = + baked light by fifths of this room's range; ? no lightmap):");
            for (int z = z1; z >= z0; z--)
            {
                var row = new System.Text.StringBuilder("    ");
                for (int x = x0; x <= x1; x++)
                {
                    var c = new Vector2Int(x, z);
                    if (!cells.TryGetValue(c, out var v)) { row.Append(room.Contains(c) ? '?' : ' '); continue; }
                    row.Append(v.lit ? '#' : v.baked < 0f ? '?' : shades[Mathf.Clamp(vals.FindIndex(q => q >= v.baked) * 5 / Mathf.Max(1, vals.Count), 0, 4)]);
                }
                sb.AppendLine(row.ToString().TrimEnd());
            }
        }

        /// <summary>
        /// The pilot's rooms reflect themselves. Every house and shop has a box reflection probe showing one small warm room
        /// cubemap (FPSKitTownGI.PlaceIndoorReflections), set for the rooms' old brightness; under the adapted eye it laid an
        /// even milky sheen over a lightmapped room's walls and floor that hid the fall-off from its openings (the views with
        /// every reflection off showed it, 2026-10-31). The pilot's rooms' probes are captured from the room's middle at
        /// 1.5 m (the shop's middle), with box projection, after the bakes, so they reflect the room as lit. Needs a graphics
        /// device (UNITY_GRAPHICS=1): captured headless, probes drew everything flat white.
        /// </summary>
        public static void CaptureReflections(string scenePath)
        {
            var pilot = GameObject.Find("LightmapPilot");
            var root = Object.FindAnyObjectByType<IndoorReflections>(FindObjectsInactive.Include);
            if (pilot == null || root == null) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Debug.LogWarning("[FPSKit] lightmap pilot: no graphics device, room reflections not captured"); return; }
            string folder = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
            int done = 0;
            foreach (Transform view in pilot.transform)
            {
                var look = view.Find("Look");
                if (look == null || view.name.EndsWith("_openings")) continue;
                bool shop = view.name == "shop";
                Vector3 inRoom = shop ? look.position : look.position + Vector3.up * 0.2f;
                // The probe whose box holds the room: the smallest.
                var probe = probes.Where(p => new Bounds(p.transform.position + p.center, p.size).Contains(inRoom))
                                  .OrderBy(p => p.size.x * p.size.y * p.size.z).FirstOrDefault();
                if (probe == null) { Debug.LogWarning($"[FPSKit] lightmap pilot: no room probe at {view.name}"); continue; }
                var box = new Bounds(probe.transform.position + probe.center, probe.size);
                var capture = shop ? look.position : look.position + Vector3.up * 1.2f;
                probe.transform.position = capture;
                probe.center = box.center - capture;
                probe.boxProjection = true;
                probe.resolution = 128;
                bool was = probe.enabled;
                probe.enabled = true;
                string path = $"{folder}/PilotReflection_{view.name}.exr";
                if (Lightmapping.BakeReflectionProbe(probe, path))
                {
                    AssetDatabase.ImportAsset(path);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    if (tex != null) { probe.customBakedTexture = tex; done++; }
                }
                probe.enabled = was;
            }
            Debug.Log($"[FPSKit] lightmap pilot: {done} room reflections captured");
        }

        /// <summary>After the bake: the lightmaps made, their size and memory, and what they cover.</summary>
        public static string Report(Plan plan)
        {
            if (plan == null) return "no lightmap pilot";
            var maps = LightmapSettings.lightmaps;
            long bytes = 0;
            var sizes = new List<string>();
            foreach (var m in maps)
                foreach (var tex in new Texture2D[] { m.lightmapColor, m.lightmapDir, m.shadowMask })
                {
                    if (tex == null) continue;
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                    sizes.Add($"{tex.width}x{tex.height} {tex.format}");
                }
            int got = plan.lit.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
            // The whole town's insides by kind, for what a town-wide bake would make.
            float Area(string group, string prefix)
            {
                var g = GameObject.Find("Arena/Buildings/" + group);
                if (g == null) return 0f;
                float sum = 0f;
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.name.StartsWith(prefix) || mf.sharedMesh == null) continue;
                    var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                    for (int i = 0; i < t.Length; i += 3) sum += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
                }
                return sum;
            }
            string town = $"; the town's insides: houses {Area("HouseInteriors", "Interior_"):0} m², walk-in shops {Area("ShopInteriors", "ShopInside_"):0} m², " +
                          $"shops seen only {Area("ShopInteriors", "ShopWindow_"):0} m²";
            return $"lightmap pilot: {got} of {plan.lit.Count} insides lightmapped ({string.Join(", ", plan.lit.Select(r => r.name))}), {plan.area:0} m² at {TexelsPerMetre:0} texels/m, " +
                   $"{plan.shading} pieces of furniture shading, {plan.tubes} lit shops' tubes; {maps.Length} lightmaps ({string.Join("; ", sizes)}), {bytes / 1048576f:0.0} MB" + town;
        }
    }
}
#endif
