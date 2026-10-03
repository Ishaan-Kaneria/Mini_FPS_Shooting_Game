#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The buildings of the rooftop zone: shell, roof deck, parapet, rooftop plant, the fire escapes up the
    /// facades and the skybridges between roofs. See FPSKitCity.cs for the plan.
    ///
    /// Every building is built in <b>world coordinates</b>, not around its own origin. The facade texture is
    /// planar, so a mesh built at the origin and moved would show every building the same lit windows; built
    /// in place, each reads a different part of the 53 m tile.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float DeckThick = 0.25f;
        private const float ParapetHigh = 1.1f;

        private static void StaticFlags(GameObject go)
        {
            if (go == null) return;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic |
                UnityEditor.StaticEditorFlags.OccluderStatic | UnityEditor.StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>
        /// Keeps the navigation bake out of a solid mass.
        ///
        /// <b>A closed box has an inside, as far as the bake is concerned.</b> The voxelizer rasterizes triangles, not
        /// volumes, so the yard slab under a building is open ground with the building's top face as its ceiling, and
        /// every building baked a room of navmesh nobody could reach: the spawner put enemies in them. The volume is
        /// pulled in from the walls and stops under the deck, so the roof (which must stay walkable) is outside it.
        /// </summary>
        private static void NoEnter(Transform parent, Vector3 baseCentre, float width, float depth, float height, float yaw = 0f)
        {
            var go = new GameObject("NoEnter");
            go.transform.SetParent(parent, false);
            go.transform.position = baseCentre + Vector3.up * (height * 0.5f - 0.6f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var volume = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            volume.size = new Vector3(Mathf.Max(0.5f, width), height + 1.2f, Mathf.Max(0.5f, depth));
            volume.area = 1;   // Not Walkable
        }

        // ==================================================================
        // A building
        // ==================================================================
        private static void BuildCityBuilding(Transform parent, int layer, System.Random rng, CityLot lot)
        {
            float h = lot.Height;
            var root = new GameObject($"Building_{lot.x0:0}_{lot.z0:0}").transform;
            root.SetParent(parent, false);

            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            string key = $"{lot.x0:0.0}_{lot.z0:0.0}_{lot.x1:0.0}_{lot.z1:0.0}_{lot.floors}";

            // ---- tiers: a walkable building is one block; a tower steps back once or twice ----
            var tiers = new List<(float x0, float z0, float x1, float z1, float y1)>();
            if (lot.walkable || lot.floors < 12)
                tiers.Add((lot.x0, lot.z0, lot.x1, lot.z1, h));
            else
            {
                int baseFloors = Mathf.Max(6, Mathf.RoundToInt(lot.floors * 0.55f));
                float inset = FloorH * 2f;
                tiers.Add((lot.x0, lot.z0, lot.x1, lot.z1, baseFloors * FloorH));
                if (lot.W > inset * 2f + 15f && lot.D > inset * 2f + 15f)
                {
                    int midFloors = baseFloors + Mathf.Max(2, Mathf.RoundToInt((lot.floors - baseFloors) * 0.55f));
                    tiers.Add((lot.x0 + inset, lot.z0 + inset, lot.x1 - inset, lot.z1 - inset, midFloors * FloorH));
                    if (midFloors < lot.floors && lot.W > inset * 4f + 12f && lot.D > inset * 4f + 12f)
                        tiers.Add((lot.x0 + inset * 2f, lot.z0 + inset * 2f, lot.x1 - inset * 2f, lot.z1 - inset * 2f, h));
                    else tiers[tiers.Count - 1] = (tiers[tiers.Count - 1].x0, tiers[tiers.Count - 1].z0, tiers[tiers.Count - 1].x1, tiers[tiers.Count - 1].z1, h);
                }
                else tiers[0] = (tiers[0].x0, tiers[0].z0, tiers[0].x1, tiers[0].z1, h);
            }

            var shell = new MeshBuild { UVScale = 1f / (FacadeCells * FloorH) };
            var deck = new MeshBuild { UVScale = 0.2f };
            var trim = new MeshBuild { UVScale = 0.4f };

            foreach (var t in tiers)
            {
                float w = t.x1 - t.x0, d = t.z1 - t.z0;
                var c = new Vector3((t.x0 + t.x1) * 0.5f, 0f, (t.z0 + t.z1) * 0.5f);
                // Walls up to the underside of the deck; the deck is a separate mesh with its own material.
                shell.Box(new Vector3(c.x, (t.y1 - DeckThick) * 0.5f, c.z), new Vector3(w, t.y1 - DeckThick, d), Quaternion.identity);
                deck.Box(new Vector3(c.x, t.y1 - DeckThick * 0.5f, c.z), new Vector3(w, DeckThick, d), Quaternion.identity);
                // The inside of this tier is not ground. Stops a metre under the deck so the roof is not in it.
                NoEnter(root, new Vector3(c.x, 0f, c.z), w - 1.2f, d - 1.2f, t.y1 - 1.1f);
                // A cornice under the deck, overhanging it a little.
                trim.Box(new Vector3(c.x, t.y1 - DeckThick - 0.2f, c.z), new Vector3(w + 0.5f, 0.4f, d + 0.5f), Quaternion.identity);
            }
            // The ground-floor plinth.
            trim.Box(new Vector3(lot.Centre.x, 0.45f, lot.Centre.z), new Vector3(lot.W + 0.24f, 0.9f, lot.D + 0.24f), Quaternion.identity);

            var shellGo = MeshObject(root, "Shell", ToMesh(shell, $"cityshell_{key}"), _cityFacade[lot.facade], Vector3.zero,
                                     Quaternion.identity, Vector3.one, layer, "Concrete");
            Mark(shellGo, Color.Lerp(new Color(0.20f, 0.21f, 0.24f), new Color(0.46f, 0.47f, 0.52f), Mathf.Clamp01(lot.floors / 18f)), 4);
            StaticFlags(shellGo);

            var deckGo = MeshObject(root, "Roof", ToMesh(deck, $"cityroof_{key}"), _cityRoof, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Concrete");
            StaticFlags(deckGo);
            if (!lot.walkable) { NoStanding(shellGo); NoStanding(deckGo); }

            // The shell's own top face lies under the deck, but the cornice and plinth ledges are open to the sky.
            var trimGo = MeshObject(root, "Trim", ToMesh(trim, $"citytrim_{key}"), _cityTrim, Vector3.zero, Quaternion.identity,
                                    Vector3.one, backdrop, null, collider: false);
            NoStanding(trimGo);
            StaticFlags(trimGo);

            if (lot.walkable)
            {
                BuildParapet(root, layer, lot, key);
                BuildRoofPlant(root, layer, rng, lot, key);
                if (_escapes.TryGetValue(lot, out var list))
                    foreach (var e in list) BuildEscape(root, layer, lot, e.side, e.anchor);
            }
            else BuildTowerCrown(root, layer, rng, lot, tiers[tiers.Count - 1], key);
        }

        // ==================================================================
        // Parapet
        // ==================================================================
        /// <summary>
        /// Chest-high wall round a walkable roof with an opening at every stair and bridge. It is cover, and it
        /// keeps the roof from being a flat plate with nothing to hold it against.
        /// </summary>
        private static void BuildParapet(Transform root, int layer, CityLot lot, string key)
        {
            var b = new MeshBuild { UVScale = 0.3f };
            float h = lot.Height;
            const float t = 0.35f, inset = 0.2f;

            void Run(int side, float lo, float hi, float fixedAt)
            {
                // Subtract the gaps on this face.
                var cuts = new List<(float a, float b)>();
                foreach (var g in lot.gaps)
                    if (g.side == side) cuts.Add((g.at - g.width * 0.5f, g.at + g.width * 0.5f));
                cuts.Sort((p, q) => p.a.CompareTo(q.a));

                float from = lo;
                void Seg(float a, float c)
                {
                    if (c - a < 0.3f) return;
                    bool alongX = side < 2;
                    var centre = alongX ? new Vector3((a + c) * 0.5f, h + ParapetHigh * 0.5f, fixedAt)
                                        : new Vector3(fixedAt, h + ParapetHigh * 0.5f, (a + c) * 0.5f);
                    var size = alongX ? new Vector3(c - a, ParapetHigh, t) : new Vector3(t, ParapetHigh, c - a);
                    b.Box(centre, size, Quaternion.identity);
                }
                foreach (var cut in cuts) { Seg(from, cut.a); from = Mathf.Max(from, cut.b); }
                Seg(from, hi);
            }

            Run(0, lot.x0, lot.x1, lot.z1 - inset);
            Run(1, lot.x0, lot.x1, lot.z0 + inset);
            Run(2, lot.z0, lot.z1, lot.x1 - inset);
            Run(3, lot.z0, lot.z1, lot.x0 + inset);

            var go = MeshObject(root, "Parapet", ToMesh(b, $"cityparapet_{key}_{lot.gaps.Count}"), _cityRoof, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Concrete");
            NoStanding(go);
            StaticFlags(go);
        }

        // ==================================================================
        // Rooftop plant
        // ==================================================================
        /// <summary>
        /// Air-conditioning units, water tanks, vents, skylights, dishes and an occasional stair bulkhead,
        /// scattered so a roof is somewhere with things on it, and kept four and a half metres apart and clear of
        /// every stair and bridge landing so the roof is always one connected surface. All of it is cover and all
        /// of it is off the navigation bake.
        /// </summary>
        private static void BuildRoofPlant(Transform root, int layer, System.Random rng, CityLot lot, string key)
        {
            var b = new MeshBuild { UVScale = 0.3f };
            float y = lot.Height;
            var placed = new List<Vector2>();

            // Keep-clear circles: five metres in from each gap.
            var clear = new List<Vector3>();
            foreach (var g in lot.gaps)
            {
                Vector3 p = g.side == 0 ? new Vector3(g.at, 0f, lot.z1 - 3f) : g.side == 1 ? new Vector3(g.at, 0f, lot.z0 + 3f)
                          : g.side == 2 ? new Vector3(lot.x1 - 3f, 0f, g.at) : new Vector3(lot.x0 + 3f, 0f, g.at);
                clear.Add(p);
            }

            int count = Mathf.Clamp(Mathf.RoundToInt(lot.W * lot.D / 230f), 3, 12);
            int bulkheads = 0;

            for (int i = 0, tries = 0; i < count && tries < 80; tries++)
            {
                float x = Rand(rng, lot.x0 + 3.5f, lot.x1 - 3.5f);
                float z = Rand(rng, lot.z0 + 3.5f, lot.z1 - 3.5f);
                var here = new Vector2(x, z);

                bool bad = false;
                foreach (var p in placed) if ((p - here).magnitude < 4.5f) { bad = true; break; }
                foreach (var c in clear) if (Vector2.Distance(new Vector2(c.x, c.z), here) < 6f) { bad = true; break; }
                if (bad) continue;
                placed.Add(here);
                i++;

                float kind = (float)rng.NextDouble();
                float yaw = Rand(rng, 0f, 90f);
                var rot = Quaternion.Euler(0f, yaw, 0f);

                if (kind < 0.36f)                                    // air handler with a fan on top
                {
                    b.Box(new Vector3(x, y + 0.7f, z), new Vector3(2.6f, 1.4f, 1.7f), rot);
                    b.Tube(new Vector3(x, y + 1.4f, z), new Vector3(x, y + 1.62f, z), 0.55f, 0.55f, 14);
                    b.Box(rot * new Vector3(1.0f, 0f, 0f) + new Vector3(x, y + 0.4f, z), new Vector3(0.9f, 0.8f, 1.2f), rot);
                }
                else if (kind < 0.48f)                               // water tank on legs
                {
                    foreach (var o in new[] { new Vector2(1.1f, 1.1f), new Vector2(-1.1f, 1.1f), new Vector2(1.1f, -1.1f), new Vector2(-1.1f, -1.1f) })
                    {
                        var q = rot * new Vector3(o.x, 0f, o.y);
                        b.Tube(new Vector3(x + q.x, y, z + q.z), new Vector3(x + q.x, y + 2.6f, z + q.z), 0.11f, 0.11f, 6);
                    }
                    b.Tube(new Vector3(x, y + 2.6f, z), new Vector3(x, y + 5.4f, z), 1.45f, 1.45f, 14);
                    b.Tube(new Vector3(x, y + 5.4f, z), new Vector3(x, y + 6.3f, z), 1.45f, 0.15f, 14);
                }
                else if (kind < 0.62f)                               // vent stack
                {
                    b.Tube(new Vector3(x, y, z), new Vector3(x, y + 2.6f, z), 0.38f, 0.34f, 10);
                    b.Tube(new Vector3(x, y + 2.6f, z), new Vector3(x, y + 3.0f, z), 0.62f, 0.62f, 10);
                }
                else if (kind < 0.76f)                               // skylight
                {
                    b.Box(new Vector3(x, y + 0.3f, z), new Vector3(3.2f, 0.6f, 2.0f), rot);
                    b.Box(new Vector3(x, y + 0.72f, z), new Vector3(2.6f, 0.28f, 1.5f), rot);
                }
                else if (kind < 0.88f)                               // satellite dish
                {
                    b.Tube(new Vector3(x, y, z), new Vector3(x, y + 1.8f, z), 0.09f, 0.09f, 6);
                    b.Box(new Vector3(x, y + 2.1f, z) + rot * new Vector3(0.25f, 0f, 0f), new Vector3(1.6f, 0.08f, 1.6f), rot * Quaternion.Euler(0f, 0f, 35f));
                }
                else if (bulkheads < 1 && lot.W * lot.D > 900f)     // a stair bulkhead: the roof door
                {
                    b.Box(new Vector3(x, y + 1.55f, z), new Vector3(4.2f, 3.1f, 3.6f), rot);
                    b.Box(new Vector3(x, y + 3.2f, z), new Vector3(4.6f, 0.25f, 4.0f), rot);
                    // The deck under a bulkhead has three metres of clearance over it, which bakes as a closed room of floor.
                    var nogo = new GameObject("NoEnterBulkhead");
                    nogo.transform.SetParent(root, false);
                    nogo.transform.position = new Vector3(x, y + 1.5f, z);
                    nogo.transform.rotation = rot;
                    var vol = nogo.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
                    vol.size = new Vector3(3.6f, 3.4f, 3.0f); vol.area = 1;
                    bulkheads++;
                }
                else                                                 // a long duct run
                {
                    b.Box(new Vector3(x, y + 0.55f, z), new Vector3(6.5f, 0.7f, 0.9f), rot);
                    b.Box(new Vector3(x, y + 0.2f, z), new Vector3(0.3f, 0.4f, 1.2f), rot);
                }
            }

            AddRoofLights(b, lot, placed, clear, rng);

            var go = MeshObject(root, "RoofPlant", ToMesh(b, $"cityplant_{key}"), _cityPlant, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Metal");
            NoStanding(go);
            StaticFlags(go);
        }

        /// <summary>The crown of a tower: an antenna with a red aircraft beacon, and a few units on the roof.</summary>
        private static void BuildTowerCrown(Transform root, int layer, System.Random rng, CityLot lot,
                                            (float x0, float z0, float x1, float z1, float y1) top, string key)
        {
            var b = new MeshBuild { UVScale = 0.3f };
            var beacon = new MeshBuild { UVScale = 1f };
            float y = top.y1;
            float cx = (top.x0 + top.x1) * 0.5f, cz = (top.z0 + top.z1) * 0.5f;

            float mast = Rand(rng, 9f, 16f);
            b.Tube(new Vector3(cx, y, cz), new Vector3(cx, y + mast, cz), 0.22f, 0.07f, 8);
            b.Box(new Vector3(cx, y + mast * 0.6f, cz), new Vector3(2.4f, 0.1f, 0.1f), Quaternion.identity);
            b.Box(new Vector3(cx, y + mast * 0.8f, cz), new Vector3(1.6f, 0.1f, 0.1f), Quaternion.identity);
            beacon.Box(new Vector3(cx, y + mast + 0.25f, cz), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity);

            for (int i = 0; i < 4; i++)
            {
                float x = Rand(rng, top.x0 + 4f, top.x1 - 4f), z = Rand(rng, top.z0 + 4f, top.z1 - 4f);
                b.Box(new Vector3(x, y + 0.9f, z), new Vector3(3.2f, 1.8f, 2.4f), Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
            }

            var go = MeshObject(root, "Crown", ToMesh(b, $"citycrown_{key}"), _cityPlant, Vector3.zero, Quaternion.identity, Vector3.one,
                                layer, "Metal", collider: false);
            NoStanding(go);
            StaticFlags(go);
            var lamp = MeshObject(root, "Beacon", ToMesh(beacon, $"citybeacon_{key}"), _cityNeon[4], Vector3.zero, Quaternion.identity, Vector3.one,
                                  layer, null, collider: false);
            NoStanding(lamp);
        }

        // ==================================================================
        // Fire escape
        // ==================================================================
        /// <summary>
        /// A zig-zag fire escape from the pavement to the roof, up one face.
        ///
        /// Two lanes side by side, each 2.2 m wide (the agent is 1 m across and the bake erodes half a metre off every edge, so the first cut at 1.6 m baked some flights and not others), with
        /// a landing at the end of every flight spanning both: flight one climbs lane A in the +u direction,
        /// the landing turns the climber into lane B going back, the next landing turns them into lane A again.
        /// Each flight is 15 treads of 0.22 m rise and 0.30 m tread, one floor, which is the step height the
        /// agent is known to climb. The last landing runs 1.8 m in over the roof deck at the same height, so the
        /// bake joins the two surfaces, and the parapet is open where it lands. Built in a local frame (x along
        /// the face, z out from it) and turned onto the face, so one mesh per floor count serves every building.
        /// </summary>
        private static void BuildEscape(Transform parent, int layer, CityLot lot, int side, float anchor)
        {
            EnsureStreetMaterials();
            int n = lot.floors;
            var b = new MeshBuild { UVScale = 0.5f };
            var lamps = new MeshBuild { UVScale = 1f };
            var guardList = new List<(Vector3 c, Vector3 size, float tilt)>();
            void Guard(Vector3 c, Vector3 size, float tilt) => guardList.Add((c, size, tilt));

            const float laneA0 = 0.4f, laneA1 = 2.6f, laneB0 = 2.8f, laneB1 = 5.0f, mid = 2.7f, wide = 2.2f, land = EscapeLanding;
            const float tread = 0.30f, rise = 0.22f, run = 15 * tread;

            float theta = Mathf.Atan2(FloorH, run) * Mathf.Rad2Deg;
            float stringerLen = Mathf.Sqrt(run * run + FloorH * FloorH);

            for (int f = 0; f < n; f++)
            {
                bool lane = f % 2 == 0;                     // true: lane A, climbing +u
                float y0 = f * FloorH;
                float zc = lane ? (laneA0 + laneA1) * 0.5f : (laneB0 + laneB1) * 0.5f;

                for (int k = 0; k < 15; k++)
                {
                    float top = y0 + (k + 1) * rise;
                    float u = lane ? (k + 0.5f) * tread : run - (k + 0.5f) * tread;
                    b.Box(new Vector3(u, top - 0.05f, zc), new Vector3(tread, 0.1f, wide), Quaternion.identity);
                }

                // Two stringers under the treads and the open-side rail.
                float rot = lane ? theta : -theta;
                foreach (float off in new[] { -0.95f, 0.95f })
                    b.Box(new Vector3(run * 0.5f, y0 + FloorH * 0.5f - 0.18f, zc + off), new Vector3(stringerLen, 0.2f, 0.05f), Quaternion.Euler(0f, 0f, rot));
                // Handrails on both sides of the flight (waist height), a toe plate under each, and a baluster every
                // second tread. The invisible guards below make the same sides solid, so a missed turn is a bump, not a fall.
                float railA = lane ? laneA0 + 0.03f : laneB0 + 0.03f, railB = lane ? laneA1 + 0.03f : laneB1 - 0.03f;
                foreach (float rz in new[] { railA, railB })
                {
                    b.Box(new Vector3(run * 0.5f, y0 + FloorH * 0.5f + 1.00f, rz), new Vector3(stringerLen, 0.07f, 0.07f), Quaternion.Euler(0f, 0f, rot));
                    b.Box(new Vector3(run * 0.5f, y0 + FloorH * 0.5f + 0.12f, rz), new Vector3(stringerLen, 0.12f, 0.03f), Quaternion.Euler(0f, 0f, rot));
                    for (int k = 0; k <= 15; k += 2)
                    {
                        float u = lane ? k * tread : run - k * tread;
                        b.Box(new Vector3(u, y0 + k * rise + 0.55f, rz), new Vector3(0.035f, 1.0f, 0.035f), Quaternion.identity);
                    }
                }

                // Invisible guards: solid, 1.2 m above the treads, on both sides of the flight.
                foreach (float gz in new[] { railA - 0.03f, railB + 0.03f - (lane ? 0.06f : 0f) })
                    Guard(new Vector3(run * 0.5f, y0 + FloorH * 0.5f + 0.55f, gz), new Vector3(stringerLen, 1.3f, 0.14f), rot);

                // The landing at the end of this flight.
                float ly = (f + 1) * FloorH;
                float lu0 = lane ? run : -land, lu1 = lane ? run + land : 0f;
                bool last = f == n - 1;
                float z0 = last ? -1.8f : laneA0, z1 = laneB1;
                b.Box(new Vector3((lu0 + lu1) * 0.5f, ly - 0.05f + (last ? 0.01f : 0f), (z0 + z1) * 0.5f), new Vector3(land, 0.12f, z1 - z0), Quaternion.identity);

                // Landing rails: the outer edge and the far end, with balusters; the top landing is open to the roof.
                float uEnd = lane ? lu1 : lu0;
                float lcx = (lu0 + lu1) * 0.5f, lcz = (laneA0 + z1) * 0.5f;
                b.Box(new Vector3(lcx, ly + 1.0f, z1 - 0.03f), new Vector3(land, 0.07f, 0.07f), Quaternion.identity);
                b.Box(new Vector3(uEnd - (lane ? 0.03f : -0.03f), ly + 1.0f, lcz), new Vector3(0.07f, 0.07f, z1 - laneA0), Quaternion.identity);
                for (float bu = lu0 + 0.15f; bu <= lu1; bu += 0.3f)
                    b.Box(new Vector3(bu, ly + 0.52f, z1 - 0.03f), new Vector3(0.035f, 1.0f, 0.035f), Quaternion.identity);
                for (float bz = laneA0 + 0.15f; bz <= z1; bz += 0.3f)
                    b.Box(new Vector3(uEnd - (lane ? 0.03f : -0.03f), ly + 0.52f, bz), new Vector3(0.035f, 1.0f, 0.035f), Quaternion.identity);
                b.Box(new Vector3(lcx, ly + 0.10f, z1 - 0.03f), new Vector3(land, 0.12f, 0.03f), Quaternion.identity);
                Guard(new Vector3(lcx, ly + 0.6f, z1 + 0.05f), new Vector3(land + 0.2f, 1.2f, 0.14f), 0f);
                Guard(new Vector3(uEnd + (lane ? 0.05f : -0.05f), ly + 0.6f, lcz), new Vector3(0.14f, 1.2f, z1 - laneA0 + 0.2f), 0f);

                // A warm lamp cage at the outer corner of every landing: a visible marker for where the stairs turn.
                lamps.Box(new Vector3(uEnd - (lane ? 0.2f : -0.2f), ly + 2.3f, z1 - 0.2f), new Vector3(0.26f, 0.2f, 0.26f), Quaternion.identity);
                b.Tube(new Vector3(uEnd - (lane ? 0.2f : -0.2f), ly, z1 - 0.2f), new Vector3(uEnd - (lane ? 0.2f : -0.2f), ly + 2.2f, z1 - 0.2f), 0.04f, 0.04f, 6);
            }

            Vector3 at = FacePoint(lot, side, anchor);
            var go = MeshObject(parent, "FireEscape", ToMesh(b, $"cityescape_{n}"), _zoneSteel, at, Quaternion.Euler(0f, FaceYaw(side), 0f),
                                Vector3.one, layer, "Metal");
            StaticFlags(go);

            // Guards: collider-only children (no renderer, so the bake ignores them), turned with the escape.
            var guards = new GameObject("Guards").transform;
            guards.SetParent(go.transform, false);
            foreach (var g in guardList)
            {
                var gg = new GameObject("Guard");
                gg.layer = layer;
                gg.transform.SetParent(guards, false);
                gg.transform.localPosition = g.c;
                gg.transform.localRotation = Quaternion.Euler(0f, 0f, g.tilt);
                gg.AddComponent<BoxCollider>().size = g.size;
            }

            var lampGo = MeshObject(go.transform, "Lamps", ToMesh(lamps, $"cityescapelamp_{n}"), _cityLamp, Vector3.zero, Quaternion.identity, Vector3.one,
                                    layer, null, collider: false);
            NoStanding(lampGo);
            lampGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ==================================================================
        // Skybridge
        // ==================================================================
        /// <summary>
        /// A three-metre deck between two roofs of the same height, with a rail down each side over the gap and a
        /// truss under it. It overlaps each roof by a metre and a half, which is what joins the bake.
        /// </summary>
        private static void BuildSkybridge(Transform parent, int layer, CityBridge br)
        {
            float length = (br.to - br.from) + 3.2f;
            float y = br.a.Height;
            var b = new MeshBuild { UVScale = 0.5f };

            b.Box(new Vector3(length * 0.5f, -0.09f, 0f), new Vector3(length, 0.2f, 3.4f), Quaternion.identity);
            foreach (float z in new[] { -1.4f, 1.4f })
            {
                b.Box(new Vector3(length * 0.5f, -0.45f, z), new Vector3(length, 0.5f, 0.22f), Quaternion.identity);
            }
            for (float u = 2.2f; u < length - 1f; u += 3.6f)
                b.Box(new Vector3(u, -0.3f, 0f), new Vector3(0.14f, 0.14f, 3.0f), Quaternion.identity);

            // Rails over the gap only: the roofs at both ends are open.
            float r0 = 1.6f, r1 = length - 1.6f;
            foreach (float z in new[] { -1.65f, 1.65f })
            {
                b.Box(new Vector3((r0 + r1) * 0.5f, 1.0f, z), new Vector3(r1 - r0, 0.07f, 0.07f), Quaternion.identity);
                b.Box(new Vector3((r0 + r1) * 0.5f, 0.5f, z), new Vector3(r1 - r0, 0.05f, 0.05f), Quaternion.identity);
                for (float u = r0; u <= r1 + 0.01f; u += 2f)
                    b.Box(new Vector3(u, 0.5f, z), new Vector3(0.06f, 1.0f, 0.06f), Quaternion.identity);
            }

            Vector3 at = br.alongX ? new Vector3(br.from - 1.6f, y + 0.01f, br.at) : new Vector3(br.at, y + 0.01f, br.from - 1.6f);
            float yaw = br.alongX ? 0f : 270f;
            var go = MeshObject(parent, "Skybridge", ToMesh(b, $"citybridge_{length:0.0}"), _zoneSteel, at, Quaternion.Euler(0f, yaw, 0f),
                                Vector3.one, layer, "Metal");
            StaticFlags(go);
        }
    }
}
#endif
