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

        /// <summary>Half the side of the district round the player's start whose insides are lightmapped too (0: the pilot's
        /// four alone). 200 m: about a sixth of the town, the first district (2026-11-03).</summary>
        const float DistrictHalf = 200f;

        /// <summary>The district's texels against the pilot's: 0.5, 20 cm, so a sixth of the town fits this laptop's bake.</summary>
        const float DistrictScale = 0.5f;

        /// <summary>A shop tube's brightness as a baked area light (a 6 cm by 1.2 m strip under the tube). At 8 the tubes showed
        /// and lit almost nothing: the back wall came out darker than the flat paint it replaced (2026-10-31).</summary>
        const float TubeIntensity = 20f;

        /// <summary>A cool white, as the tubes in the reference shops (Build/Realism/reference_interiors/shop_01-03).</summary>
        static readonly Color TubeColour = new Color(0.94f, 0.97f, 1f);

        public sealed class Plan
        {
            public readonly List<MeshRenderer> lit = new List<MeshRenderer>();
            public int shading, tubes, district;
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
            var props = arena.transform.Find("Buildings/BuildingProps")?.GetComponentsInChildren<MeshRenderer>(true) ?? new MeshRenderer[0];
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
                LightInside(inside, shop, 1f);
            }

            // The district (2026-11-03, his pick: "one district first"): every house's and shop's inside within DistrictHalf of
            // the player's start, at 20 cm (DistrictScale), so the rooms there take their light from lightmaps as the pilot's
            // do (daylight factor 2-6 % near the openings, 0.5-1.8 % at the back, as real rooms) instead of the 3 m probes,
            // which leak the light over the roofs into the upper walls and hold almost none of a room's own.
            int district = 0;
            if (DistrictHalf > 0f)
            {
                var start = player.transform.position;
                foreach (var (group, shop) in new[] { (houses, false), (shops, true) })
                    if (group != null)
                        foreach (var r in group.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (r.name.StartsWith("Detail_") || plan.lit.Contains(r)) continue;
                            var c = r.bounds.center;
                            if (Mathf.Abs(c.x - start.x) > DistrictHalf || Mathf.Abs(c.z - start.z) > DistrictHalf) continue;
                            LightInside(r, shop, DistrictScale);
                            district++;
                        }
            }
            plan.district = district;

            void LightInside(MeshRenderer inside, bool shop, float scale)
            {
                if (plan.lit.Contains(inside)) return;
                plan.lit.Add(inside);
                plan.area += Lightmapped(inside);
                inside.scaleInLightmap *= scale;

                // The furniture in it shades the bake, lit itself by the probes as before.
                var space = inside.bounds;
                foreach (var r in props)
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

        static float s_outdoor;     // the open ground's light for the report in progress (set at its start)

        static string LightReport()
        {
            var sb = new System.Text.StringBuilder();
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            sb.AppendLine($"sun: elevation {Mathf.Asin(toSun.y) * Mathf.Rad2Deg:0.0}, from azimuth {Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg:0} (0 north, 90 east)");
            // The open ground's light, in the lightmap's units (what multiplies the albedo): the sun on a level floor plus the
            // sky from above (the ambient probe). A room's floor against it is its daylight factor -- real rooms lit from one
            // side run about 2-5 % near the window and 0.5-2 % at the back (CIBSE LG10; a room under 0.5 % reads as unlit).
            float sunOnGround = sun != null ? Lum(sun.color.linear) * sun.intensity * Mathf.Max(0f, toSun.y) : 0f;
            var skyUp = new Vector3[] { Vector3.up }; var skyCol = new Color[1];
            RenderSettings.ambientProbe.Evaluate(skyUp, skyCol);
            s_outdoor = sunOnGround + Lum(skyCol[0]);
            sb.AppendLine($"open ground: {s_outdoor:0.000} (sun {sunOnGround:0.000} + sky {Lum(skyCol[0]):0.000}); daylight factor = floor / open ground");
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
                if (look != null && !view.name.EndsWith("_openings") && !view.name.EndsWith("_doorway")) RoomReport(sb, view.name, view.name == "shop" ? look.position : view.position, toSun, mask, Baked);
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
                if (band.Count > 0) sb.AppendLine($"    {from:0}-{(to > 50 ? "" : to.ToString("0"))} m from an opening to the open air: mean {band.Average():0.000} ({band.Count})" +
                                                  (s_outdoor > 0f ? $", daylight factor {100f * band.Average() / s_outdoor:0.00} %" : ""));
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
        /// The pilot's rooms reflect themselves, as bright as they are lit. Every house and shop has a box reflection probe
        /// showing one small warm room cubemap (FPSKitTownGI.PlaceIndoorReflections). Each pilot room gets a probe of its own,
        /// boxed to the room (its floor as FindRoom finds it, floor to ceiling), box projected, captured from the room's
        /// middle at 1.5 m at the end of the probe volume bake (the shop keeps its own shop's probe). The capture is then set
        /// to the room's own brightness: its typical texel (the median of the 768 x 128 strip) brought to the light the
        /// room's lightmap says arrives, times the room's albedo. Captured as it came, a room's reflection was 17-24x the
        /// room (median 0.28-0.41 against 0.014-0.024 arriving: the walls in it looked lit by the open sky, and the
        /// reflection's sheen matched the walls' own light, the milky look he saw, 2026-10-31). Captured in the light-look
        /// refresh right after the probes were placed, the mud walls read wet and grey-blue; under Low a room came out too
        /// bright for the eye to bring down. Needs a graphics device (UNITY_GRAPHICS=1).
        /// </summary>
        public static void CaptureReflections(string scenePath)
        {
            var pilot = GameObject.Find("LightmapPilot");
            var root = Object.FindAnyObjectByType<IndoorReflections>(FindObjectsInactive.Include);
            if (pilot == null || root == null) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Debug.LogWarning("[FPSKit] lightmap pilot: no graphics device, room reflections not captured"); return; }
            string folder = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian");
            var maps = ReadableLightmaps();
            var lines = new List<string>();
            _captured.Clear();
            // The sun's direct light off while capturing: a reflection carries the room's bounced and sky light, not the
            // patch the sun throws on the floor at full strength (his ask, 2026-10-31).
            var realtimeSun = sun != null && sun.enabled ? sun : null;
            if (realtimeSun != null) realtimeSun.enabled = false;
            foreach (Transform view in pilot.transform)
            {
                var look = view.Find("Look");
                if (look == null || view.name.EndsWith("_openings") || view.name.EndsWith("_doorway")) continue;
                bool shop = view.name == "shop";
                ReflectionProbe probe;
                Vector3 capture;
                if (shop)
                {
                    probe = root.GetComponentsInChildren<ReflectionProbe>(true).Where(p => new Bounds(p.transform.position + p.center, p.size).Contains(look.position))
                                .OrderBy(p => p.size.x * p.size.y * p.size.z).FirstOrDefault();
                    if (probe == null) { Debug.LogWarning("[FPSKit] lightmap pilot: no shop probe"); continue; }
                    capture = look.position;
                }
                else
                {
                    var room = FindRoom(view.position, toSun, mask);
                    if (room.cells.Count * RoomShape.Cell * RoomShape.Cell < 4f) room = FindRoom(look.position, toSun, mask);
                    if (room.cells.Count == 0) continue;
                    float half = RoomShape.Cell * 0.5f + 0.1f;
                    var min = new Vector3(room.cells.Min(c => c.x) * RoomShape.Cell - half, room.floorY - 0.1f, room.cells.Min(c => c.y) * RoomShape.Cell - half);
                    var max = new Vector3(room.cells.Max(c => c.x) * RoomShape.Cell + half, room.ceilY + 0.1f, room.cells.Max(c => c.y) * RoomShape.Cell + half);
                    var box = new Bounds((min + max) * 0.5f, max - min);
                    capture = room.Centroid + Vector3.up * 1.5f;
                    var old = root.transform.Find("PilotRoomProbe_" + view.name);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var go = new GameObject("PilotRoomProbe_" + view.name);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = capture;
                    probe = go.AddComponent<ReflectionProbe>();
                    probe.mode = ReflectionProbeMode.Custom;
                    probe.size = box.size;
                    probe.center = box.center - capture;
                    probe.blendDistance = 0.05f;
                    probe.importance = 3;               // over its house's probe
                }
                probe.transform.position = capture;
                probe.boxProjection = true;
                probe.resolution = 128;
                probe.intensity = 1f;
                bool enabledWas = probe.enabled;
                probe.enabled = true;
                string path = $"{folder}/PilotReflection_{view.name}.exr";
                bool baked = Lightmapping.BakeReflectionProbe(probe, path);
                probe.enabled = shop && enabledWas;     // IndoorReflections switches them on near the eye
                if (!baked) continue;
                AssetDatabase.ImportAsset(path);
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (tex == null) continue;
                probe.customBakedTexture = tex;
                // To the room's brightness: the light arriving (its lightmap) times what its surfaces give back.
                var lums = StripLuminances(path);
                var inside = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.lightmapIndex >= 0 && r.lightmapIndex < maps.Length && maps[r.lightmapIndex] != null && r.bounds.Contains(look.position) && !r.name.StartsWith("Detail_"))
                    .OrderBy(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
                if (lums.Count == 0 || inside == null) continue;
                float median = lums[lums.Count / 2], arriving = LightmapMean(inside, maps), albedo = AlbedoMean(inside);
                // Captured as the room is lit (its lightmaps, the probe volumes, no direct sun), the reflection is the room:
                // at full strength. (Scaled to an estimate of the room's brightness it came out three to eight times darker
                // than the capture, and metal read black.) The estimate is logged beside it for the report.
                probe.intensity = 1f;
                _captured.Add(new Captured
                {
                    name = view.name, probeName = probe.name, shop = shop, capture = capture,
                    box = new Bounds(probe.transform.position + probe.center, probe.size), texture = path, intensity = probe.intensity,
                });
                lines.Add($"{view.name}: capture median {median:0.000} against the room's estimate {arriving:0.000} arriving x albedo {albedo:0.00} = {arriving * albedo:0.000}");
            }
            if (realtimeSun != null) realtimeSun.enabled = true;
            foreach (var m in maps) if (m != null) Object.DestroyImmediate(m);
            Debug.Log($"[FPSKit] lightmap pilot: {lines.Count} room reflections captured (under {QualitySettings.names[QualitySettings.GetQualityLevel()]}): " + string.Join("; ", lines));
        }

        /// <summary>
        /// Batch (UNITY_GRAPHICS=1, -force-vulkan): the pilot rooms' reflections captured again on the saved scene, without a
        /// light bake: the lightmaps and the probe volumes' data as they are. The probe data is loaded first -- frames drawn
        /// under High from each pilot room until no cell is left to load: captured before it was loaded, the rooms came out
        /// lit by the sky ambient (the wet, grey-blue walls of 2026-10-31). Saves the scene.
        /// </summary>
        public static void BatchCaptureReflections()
        {
            _captureScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            _captureWas = QualitySettings.GetQualityLevel();
            int low = System.Array.IndexOf(QualitySettings.names, "Low"), high = System.Array.IndexOf(QualitySettings.names, "High");
            // URP makes the probe volumes when a pipeline that has them first renders (another tier's first).
            if (low >= 0) { QualitySettings.SetQualityLevel(low, true); DrawFrame(); }
            if (high >= 0) QualitySettings.SetQualityLevel(high, true);
            _captureTicks = 0;
            EditorApplication.update += CaptureTick;
        }

        static UnityEngine.SceneManagement.Scene _captureScene;
        static int _captureWas, _captureTicks;

        /// <summary>
        /// One editor update of BatchCaptureReflections: a frame drawn from one of the pilot rooms in turn, until the editor
        /// has gone round at least 30 times and the probe data has no cell left to load; then the captures. Captured in the
        /// same update as the scene was opened, the probes drew the sky and the trees and none of the town (the wet,
        /// grey-blue walls, and four captures with the same median, 2026-10-31): the town's renderers come in over the
        /// editor's first updates; the captures at the end of a probe volume bake, many updates later, were right.
        /// </summary>
        static void CaptureTick()
        {
            try
            {
                _captureTicks++;
                var pilot = GameObject.Find("LightmapPilot");
                var spots = pilot == null ? new List<Vector3>() : pilot.transform.Cast<Transform>().Select(t => t.position + Vector3.up * 1.6f).ToList();
                var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
                if (cam != null && spots.Count > 0) cam.transform.position = spots[_captureTicks % spots.Count];
                DrawFrame();
                var pending = typeof(ProbeReferenceVolume).GetProperty("hasUnloadedCells", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                bool loading = pending == null || ProbeReferenceVolume.instance == null || !ProbeReferenceVolume.instance.isInitialized ||
                               (bool)pending.GetValue(ProbeReferenceVolume.instance);
                if (_captureTicks < 30 || (loading && _captureTicks < 600)) return;
                EditorApplication.update -= CaptureTick;
                Debug.Log($"[FPSKit] lightmap pilot: probe volume data {(loading ? "NOT all loaded" : "loaded")} after {_captureTicks} editor updates");
                CaptureReflections(FPSKitTownGI.DesertScene);
                QualitySettings.SetQualityLevel(_captureWas, true);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(_captureScene, FPSKitTownGI.DesertScene);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(loading ? 2 : 0);
            }
            catch (System.Exception e)
            {
                EditorApplication.update -= CaptureTick;
                Debug.LogError("[FPSKit] capture FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void DrawFrame()
        {
            var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (cam == null) return;
            var rt = RenderTexture.GetTemporary(128, 128, 24);
            try { RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt }); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>Batch: each pilot room's captured reflection written out as a picture to look at (its 768 x 128 strip,
        /// x / (1 + x) tone mapped, gamma encoded) in Build/Pilot/refl_&lt;room&gt;.png.</summary>
        public static void BatchDumpReflections()
        {
            string folder = FPSKitTownGI.DesertScene.Substring(0, FPSKitTownGI.DesertScene.Length - ".unity".Length);
            System.IO.Directory.CreateDirectory("Build/Pilot");
            foreach (var name in new[] { "room_1", "room_2", "room_3", "shop" })
            {
                string path = $"{folder}/PilotReflection_{name}.exr";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                var shape = importer.textureShape; bool readable = importer.isReadable; var comp = importer.textureCompression;
                importer.textureShape = TextureImporterShape.Texture2D; importer.isReadable = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var strip = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var px = strip.GetPixels();
                var outTex = new Texture2D(strip.width, strip.height, TextureFormat.RGB24, false);
                outTex.SetPixels(px.Select(c => new Color(Mathf.LinearToGammaSpace(c.r / (1f + c.r)), Mathf.LinearToGammaSpace(c.g / (1f + c.g)), Mathf.LinearToGammaSpace(c.b / (1f + c.b)))).ToArray());
                outTex.Apply();
                System.IO.File.WriteAllBytes($"Build/Pilot/refl_{name}.png", outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                importer.textureShape = shape; importer.isReadable = readable; importer.textureCompression = comp;
                importer.SaveAndReimport();
            }
            EditorApplication.Exit(0);
        }

        /// <summary>A pilot room's capture, kept to be put into the scene again (ApplyReflections): the bake that captures it
        /// runs on a stripped copy of the scene that is not saved.</summary>
        sealed class Captured
        {
            public string name, probeName, texture;
            public bool shop;
            public Vector3 capture;
            public Bounds box;
            public float intensity;
        }
        static readonly List<Captured> _captured = new List<Captured>();

        /// <summary>
        /// The pilot rooms' captures (CaptureReflections) put into the open scene: each room's own probe made again, boxed,
        /// box projected, its capture and its level; the shop's probe given its capture and level. Returns how many.
        /// </summary>
        public static int ApplyReflections()
        {
            var root = Object.FindAnyObjectByType<IndoorReflections>(FindObjectsInactive.Include);
            if (root == null || _captured.Count == 0) return 0;
            int applied = 0;
            foreach (var c in _captured)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(c.texture);
                if (tex == null) continue;
                ReflectionProbe probe;
                if (c.shop) probe = root.GetComponentsInChildren<ReflectionProbe>(true).FirstOrDefault(p => p.name == c.probeName);
                else
                {
                    var old = root.transform.Find(c.probeName);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var go = new GameObject(c.probeName);
                    go.transform.SetParent(root.transform, false);
                    probe = go.AddComponent<ReflectionProbe>();
                    probe.mode = ReflectionProbeMode.Custom;
                    probe.blendDistance = 0.05f;
                    probe.importance = 3;
                    probe.enabled = false;              // IndoorReflections switches them on near the eye
                }
                if (probe == null) continue;
                probe.transform.position = c.capture;
                probe.size = c.box.size;
                probe.center = c.box.center - c.capture;
                probe.boxProjection = true;
                probe.resolution = 128;
                probe.customBakedTexture = tex;
                probe.intensity = c.intensity;
                applied++;
            }
            Debug.Log($"[FPSKit] lightmap pilot: {applied} room reflections put into the scene");
            return applied;
        }

        /// <summary>The lightmap atlases, readable: blitted to half floats and read back.</summary>        /// <summary>The lightmap atlases, readable: blitted to half floats and read back.</summary>
        static Texture2D[] ReadableLightmaps() => LightmapSettings.lightmaps.Select(m =>
        {
            if (m.lightmapColor == null) return null;
            var t = m.lightmapColor;
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt);
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBAHalf, false, true);
            var was = RenderTexture.active; RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); copy.Apply();
            RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
            return copy;
        }).ToArray();

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The mean light arriving over a lightmapped renderer: its own part of the atlas, its empty texels left out.</summary>
        static float LightmapMean(MeshRenderer r, Texture2D[] maps)
        {
            var tex = maps[r.lightmapIndex]; var so = r.lightmapScaleOffset;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(so.z * tex.width), 0, tex.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(so.w * tex.height), 0, tex.height - 1);
            int w = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(so.x) * tex.width), 1, tex.width - x0), h = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(so.y) * tex.height), 1, tex.height - y0);
            var block = tex.GetPixels(x0, y0, w, h).Select(Lum).Where(v => v > 1e-5f).ToList();
            return block.Count > 0 ? block.Average() : 0f;
        }

        /// <summary>A renderer's surfaces' mean albedo (linear): each material's colour times its base map's mean.</summary>
        static float AlbedoMean(Renderer r)
        {
            var values = r.sharedMaterials.Where(m => m != null).Select(m =>
            {
                var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                var map = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                return Lum(c.linear) * (map != null ? Lum(TextureMean(map)) : 1f);
            }).ToList();
            return values.Count > 0 ? Mathf.Clamp(values.Average(), 0.05f, 0.9f) : 0.5f;
        }

        /// <summary>A captured reflection's texels' luminance, sorted: its 768 x 128 strip read through a readable 2D import
        /// of the file, the cube import then put back.</summary>
        static List<float> StripLuminances(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return new List<float>();
            var shape = importer.textureShape; bool readable = importer.isReadable; var comp = importer.textureCompression;
            importer.textureShape = TextureImporterShape.Texture2D; importer.isReadable = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var strip = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var lums = strip != null ? strip.GetPixels().Select(Lum).OrderBy(v => v).ToList() : new List<float>();
            importer.textureShape = shape; importer.isReadable = readable; importer.textureCompression = comp;
            importer.SaveAndReimport();
            return lums;
        }

        /// <summary>
        /// Batch (UNITY_GRAPHICS=1, -force-vulkan): why the insides look glossy (his report, 2026-10-31). Two lists, written to
        /// Build/Pilot/gloss_audit.txt:
        ///   * every material on what the four pilot views show (the pilot insides, their fine work and the furniture in
        ///     them), on the player's arms, body and guns, and on the soldier: its shader, and the smoothness and metallic it
        ///     really renders with -- a map's channel averaged on the GPU times the slider, where the material takes them
        ///     from a map;
        ///   * each pilot room's captured reflection (its 768 x 128 strip read back) against the room: its typical texel and
        ///     its brightest hundredth, the probe's intensity, and the light the room's lightmap says arrives.
        /// </summary>
        public static void BatchGlossAudit()
        {
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
                var sb = new System.Text.StringBuilder();
                MaterialAudit(sb);
                ProbeAudit(sb);
                System.IO.Directory.CreateDirectory("Build/Pilot");
                System.IO.File.WriteAllText("Build/Pilot/gloss_audit.txt", sb.ToString());
                Debug.Log("[FPSKit] gloss audit:\n" + sb);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] gloss audit FAILED: " + e); EditorApplication.Exit(1); }
        }

        /// <summary>A texture's mean, channel by channel, read back through the GPU at 64 x 64 (linear).</summary>
        static Color TextureMean(Texture tex)
        {
            if (tex == null) return Color.clear;
            var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var copy = new Texture2D(64, 64, TextureFormat.RGBAHalf, false, true);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels();
            Object.DestroyImmediate(copy);
            var sum = Color.clear;
            foreach (var c in px) sum += c;
            return sum / px.Length;
        }

        /// <summary>What a material renders with: (smoothness, metallic, where from).</summary>
        static (float smooth, float metal, string from) Surface(Material m)
        {
            float slider = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : -1f;
            float metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            var map = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            bool albedoAlpha = m.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A") || (m.HasProperty("_SmoothnessTextureChannel") && m.GetFloat("_SmoothnessTextureChannel") > 0.5f);
            if (albedoAlpha && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                return (slider * TextureMean(m.GetTexture("_BaseMap")).a, map != null ? TextureMean(map).r : metal, "base map alpha x slider");
            if (map != null)
            {
                var mean = TextureMean(map);
                return (slider * mean.a, mean.r, "metallic map (r, a x slider)");
            }
            return (slider, metal, "sliders");
        }

        static void MaterialAudit(System.Text.StringBuilder sb)
        {
            var seen = new Dictionary<Material, string>();
            void Take(string where, IEnumerable<Renderer> rs)
            {
                foreach (var r in rs)
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !seen.ContainsKey(m)) seen[m] = where + ": " + r.name;
            }
            var arena = GameObject.Find("Arena");
            var spaces = new List<Bounds>();
            foreach (var group in new[] { "Arena/Buildings/HouseInteriors", "Arena/Buildings/ShopInteriors" })
                if (GameObject.Find(group) is GameObject g)
                    foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.receiveGI == ReceiveGI.Lightmaps && r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE) spaces.Add(r.bounds);
            if (arena != null)
                Take("room", arena.GetComponentsInChildren<Renderer>(true).Where(r => spaces.Any(b => b.Contains(r.bounds.center)) && !(r is ParticleSystemRenderer)));
            foreach (var path in new[] { "Assets/FPSKit_Generated/ViewModels/FP_Arms.prefab", "Assets/FPSKit_Generated/ViewModels/FP_Body.prefab" })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) is GameObject arms) Take("player", arms.GetComponentsInChildren<Renderer>(true));
            foreach (var guid in AssetDatabase.FindAssets("VM_ t:Prefab", new[] { "Assets/FPSKit_Generated/ViewModels" }))
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)) is GameObject gun) Take("gun", gun.GetComponentsInChildren<Renderer>(true));
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab") is GameObject foe) Take("soldier", foe.GetComponentsInChildren<Renderer>(true));
            sb.AppendLine($"== materials: {seen.Count} (smoothness and metallic as rendered; sorted by smoothness)");
            sb.AppendLine("   smooth  metal  shader                               material  (where: first renderer)  [from]");
            foreach (var kv in seen.Select(kv => (kv.Key, kv.Value, s: Surface(kv.Key))).OrderByDescending(x => x.s.smooth))
                sb.AppendLine($"   {kv.s.smooth,6:0.00} {kv.s.metal,6:0.00}  {kv.Key.shader.name,-36} {kv.Key.name}  ({kv.Value})  [{kv.s.from}]");
        }

        static void ProbeAudit(System.Text.StringBuilder sb)
        {
            string folder = FPSKitTownGI.DesertScene.Substring(0, FPSKitTownGI.DesertScene.Length - ".unity".Length);
            sb.AppendLine("\n== pilot rooms' reflections against their rooms (linear; a reflection shows texel x intensity)");
            var maps = ReadableLightmaps();
            var root = Object.FindAnyObjectByType<IndoorReflections>(FindObjectsInactive.Include);
            foreach (var name in new[] { "room_1", "room_2", "room_3", "shop" })
            {
                string path = $"{folder}/PilotReflection_{name}.exr";
                var lums = StripLuminances(path);
                if (lums.Count == 0) { sb.AppendLine($"  {name}: no capture at {path}"); continue; }
                float Q(float q) => lums[Mathf.Clamp((int)(q * lums.Count), 0, lums.Count - 1)];
                var look = GameObject.Find("LightmapPilot")?.transform.Find(name)?.Find("Look");
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                var probe = root != null && tex != null ? root.GetComponentsInChildren<ReflectionProbe>(true).FirstOrDefault(p => p.customBakedTexture == tex) : null;
                float intensity = probe != null ? probe.intensity : 1f;
                float arriving = -1f, albedo = -1f;
                if (look != null)
                {
                    var inside = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                        .Where(r => r.lightmapIndex >= 0 && r.lightmapIndex < maps.Length && maps[r.lightmapIndex] != null && r.bounds.Contains(look.position) && !r.name.StartsWith("Detail_"))
                        .OrderBy(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
                    if (inside != null) { arriving = LightmapMean(inside, maps); albedo = AlbedoMean(inside); }
                }
                sb.AppendLine($"  {name}: capture median {Q(0.5f):0.000}, top 1 % from {Q(0.99f):0.00}, max {lums[lums.Count - 1]:0.0}; probe intensity {intensity:0.000} -> " +
                              $"reflected median {Q(0.5f) * intensity:0.000}; the room: {arriving:0.000} arriving x albedo {albedo:0.00} = {arriving * albedo:0.000} " +
                              $"(reflection / room {Q(0.5f) * intensity / Mathf.Max(1e-5f, arriving * albedo):0.0}x)");
            }
            foreach (var m in maps) if (m != null) Object.DestroyImmediate(m);
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
            return $"lightmap pilot: {got} of {plan.lit.Count} insides lightmapped ({string.Join(", ", plan.lit.Take(plan.lit.Count - plan.district).Select(r => r.name))}{(plan.district > 0 ? $" and {plan.district} in the district at {TexelsPerMetre * DistrictScale:0}/m" : "")}), {plan.area:0} m² at {TexelsPerMetre:0} texels/m, " +
                   $"{plan.shading} pieces of furniture shading, {plan.tubes} lit shops' tubes; {maps.Length} lightmaps ({string.Join("; ", sizes)}), {bytes / 1048576f:0.0} MB" + town;
        }
    }
}
#endif
