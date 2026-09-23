#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The plant's second layer: routes over the roofs, warehouses you can go inside, a
    /// railway siding, and machinery that moves. Asked for by Ishaan after the compounds and
    /// the infill went in -- "try to also innovate this further" -- and chosen by him from a
    /// short list.
    ///
    /// Three rules carried over from the rest of the plant, because each of them has already
    /// been a bug here:
    ///
    /// <b>Anything up high is reached by a stair, never a ladder.</b> There is no climbing in
    /// the game, for the player or for an enemy, and a roof only the player can reach is a
    /// firing position nothing ever contests -- worse, it is navmesh the spawner can find and
    /// nothing can path to. Every roof route has a stair at each end; every mezzanine has one.
    ///
    /// <b>Anything you can walk into has two ways out.</b> The warehouses have a vehicle door
    /// and two personnel doors on three different walls.
    ///
    /// <b>Moving parts carry no collider.</b> A crane trolley or a fan blade that shoves the
    /// player is a bug report; one that wedges an enemy is a level that never ends. They run
    /// overhead, and the eye is the only thing that needs them.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _cargoMat, _locoMat, _beltMat, _ballastMat, _sleeperMat;

        private static void ResolveWorksMaterials()
        {
            _cargoMat = MakeMaterial("ZoneCargo", new Color(0.58f, 0.45f, 0.30f), 0.12f, 0f);
            _locoMat = MakeMaterial("ZoneLoco", new Color(0.66f, 0.20f, 0.10f), 0.28f, 0.2f);
            _ballastMat = MakeMaterial("ZoneBallast", new Color(0.30f, 0.29f, 0.28f), 0.05f, 0f);
            _sleeperMat = MakeMaterial("ZoneSleeper", new Color(0.24f, 0.19f, 0.15f), 0.08f, 0f);

            // A rubber belt with cleats across it, so a running belt can be seen to run.
            WriteHeightPair("Belt", (u, v) =>
            {
                float cleat = Mathf.Repeat(v * 8f, 1f) < 0.12f ? 1f : 0.2f;
                return cleat * 0.8f + TileFbm(u, v, 16, 2, 9101) * 0.2f;
            }, albedoContrast: 0.6f, normalStrength: 3f);
            _beltMat = MakeDetailMaterial("Belt", new Color(0.18f, 0.18f, 0.19f), "Belt", 1f, 0.2f, 0f);
        }

        // ==================================================================
        // Roof routes
        // ==================================================================
        /// <summary>
        /// A row of two or three flat-roofed workshops along a compound wall, joined roof to
        /// roof by bridges, with a stair up at each end. It is the high road across a block:
        /// a way to flank a yard above the clutter, and chest-high parapets to fight from.
        ///
        /// The roofs are real ground. The shell is left on the bake so its top face bakes, and
        /// the seal that keeps the inside of the building off the navmesh stops a metre short
        /// of the roof -- sealed to the top, as every other closed building is, the roof would
        /// be carved away with the room under it.
        ///
        /// The height is a whole number of stair risers, so the stair's landing arrives flush
        /// with the roof rather than a few centimetres above or below it.
        ///
        /// Returns how far along the side the row reached, or the start if it did not fit.
        /// </summary>
        private static float RoofRow(Transform parent, int layer, System.Random rng, Compound c, int side,
                                     float start, float limit)
        {
            SideFrame(c, side, out _, out _, out float line, out float inward);

            int count = 2 + rng.Next(2);
            int steps = 26 + rng.Next(8);
            float height = steps * StairRise;
            float depth = Rand(rng, 11f, 14f);
            float gap = Rand(rng, 6.5f, 8f);

            var widths = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++) { widths[i] = Rand(rng, 13f, 19f); total += widths[i]; }
            total += gap * (count - 1);

            // The stairs run along the yard face of the end buildings, the way the halls'
            // do, so the row needs no ground beyond its own ends -- run out past each end,
            // they needed seventy-five metres of clear frontage and not one wall had it.
            float flight = steps * StairTread + StairLanding + 1.5f;
            for (int i = 0; i < count; i++) widths[i] = Mathf.Max(widths[i], flight + 1f);
            total = gap * (count - 1);
            foreach (float w in widths) total += w;

            float a0 = start, a1 = a0 + total;
            if (a1 > limit) return start;

            var region = SideRect(c, side, a0 - 2f, a1 + 2f, 0.35f, 0.35f + depth + 5f);
            if (SideUnderTunnel(c, side, a0) || SideUnderTunnel(c, side, a1)) return start;
            if (!ClearGround(region, height + 2f)) return start;

            Vector3 P(float a, float y, float d) => side < 2 ? new Vector3(a, y, line + inward * d) : new Vector3(line + inward * d, y, a);
            var along = side < 2 ? Vector3.right : Vector3.forward;
            float mid = 0.35f + depth * 0.5f;

            var brick = _denseBrick[rng.Next(_denseBrick.Length)];
            var roofEdges = new List<(float a, float b)>();

            float cursor = a0;
            for (int i = 0; i < count; i++)
            {
                float b0 = cursor, b1 = cursor + widths[i];
                roofEdges.Add((b0, b1));

                var centre = P((b0 + b1) * 0.5f, 0f, mid);
                var size = side < 2 ? new Vector3(widths[i], height, depth) : new Vector3(depth, height, widths[i]);

                var shell = new MeshBuild { UVScale = 0.16f };
                shell.Box(centre + Vector3.up * height * 0.5f, size, Quaternion.identity);
                MeshObject(parent, "RoofRowBuilding", ToMesh(shell, DenseKey("roofrow")), brick,
                           Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");

                // Sealed to a metre below the roof: see the summary.
                SealBox(parent, centre + Vector3.up * (height - 3f) * 0.5f,
                        new Vector3(size.x, height - 3f, size.z));

                // Windows on both long faces.
                var glass = new MeshBuild { UVScale = 0.5f };
                foreach (float d in new[] { 0.3f, 0.4f + depth })
                    for (float y = 2.3f; y < height - 1.4f; y += 3.3f)
                        for (float a = b0 + 2f; a <= b1 - 2f; a += 3.6f)
                            glass.Box(P(a, y, d), side < 2 ? new Vector3(1.6f, 1.5f, 0.12f) : new Vector3(0.12f, 1.5f, 1.6f), Quaternion.identity);
                Hide(MeshObject(parent, "RoofRowWindows", ToMesh(glass, DenseKey("roofglass")), _denseGlass,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));

                // Parapet: chest-high, open where a stair or a bridge arrives at each end.
                var parapet = new MeshBuild { UVScale = 0.3f };
                const float ph = 1.1f, pt = 0.3f, opening = 3f;
                float ya = height + ph * 0.5f;

                // The wall-side run is whole; the yard-side run is open where a stair lands.
                parapet.Box(P((b0 + b1) * 0.5f, ya, 0.35f + pt * 0.5f), side < 2 ? new Vector3(widths[i], ph, pt) : new Vector3(pt, ph, widths[i]), Quaternion.identity);

                float landAt = i == 0 ? b1 - StairLanding * 0.5f - 0.6f : b0 + StairLanding * 0.5f + 0.6f;
                bool landsHere = i == 0 || i == count - 1;
                float yardD = 0.35f + depth - pt * 0.5f;

                void YardRun(float r0, float r1)
                {
                    if (r1 - r0 < 0.2f) return;
                    parapet.Box(P((r0 + r1) * 0.5f, ya, yardD), side < 2 ? new Vector3(r1 - r0, ph, pt) : new Vector3(pt, ph, r1 - r0), Quaternion.identity);
                }

                if (landsHere) { YardRun(b0, landAt - 1.6f); YardRun(landAt + 1.6f, b1); }
                else YardRun(b0, b1);

                foreach (float a in new[] { b0 + pt * 0.5f, b1 - pt * 0.5f })
                {
                    float half = (depth - opening) * 0.5f;
                    foreach (float d in new[] { 0.35f + half * 0.5f, 0.35f + depth - half * 0.5f })
                        parapet.Box(P(a, ya, d), side < 2 ? new Vector3(pt, ph, half) : new Vector3(half, ph, pt), Quaternion.identity);
                }

                // Plant on the roof, as cover.
                for (int k = 0; k < 2; k++)
                    parapet.Box(P(Rand(rng, b0 + 3f, b1 - 3f), height + 0.7f, mid + Rand(rng, -depth * 0.25f, depth * 0.25f)),
                                new Vector3(Rand(rng, 1.6f, 2.8f), 1.4f, Rand(rng, 1.4f, 2.2f)), Quaternion.identity);

                var parapetGo = MeshObject(parent, "RoofRowParapet", ToMesh(parapet, DenseKey("roofparapet")), brick,
                                           Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
                NoStanding(parapetGo);

                cursor = b1 + gap;
            }

            // Bridges between neighbouring roofs.
            for (int i = 0; i < count - 1; i++)
            {
                var from = P(roofEdges[i].b - 0.6f, 0f, mid);
                var to = P(roofEdges[i + 1].a + 0.6f, 0f, mid);
                BuildCatwalk(parent, layer, from, to, height, stair: false);
            }

            // A stair up the yard face of each end building. The landing sits just off the
            // face, level with the roof and touching its edge, where the parapet is open:
            // the first building's stair climbs towards the row, the last one's away from it.
            float face = 0.35f + depth + StairLanding * 0.5f * 0.9f + 0.05f;
            float yawAlong = side < 2 ? 90f : 0f;

            float firstLand = roofEdges[0].b - StairLanding * 0.5f - 0.6f;
            BuildStairTower(parent, layer, P(firstLand - StairRun(height), 0f, face), height, yawAlong);

            float lastLand = roofEdges[count - 1].a + StairLanding * 0.5f + 0.6f;
            BuildStairTower(parent, layer, P(lastLand + StairRun(height), 0f, face), height, yawAlong + 180f);

            return a1;
        }

        // ==================================================================
        // Enterable warehouses
        // ==================================================================
        /// <summary>
        /// A warehouse you can go into: a vehicle door on its front, a personnel door in the
        /// back wall and another in one side, pallet racking in rows running front to back so
        /// the aisles lead through, and a mezzanine along the back wall up a stair.
        ///
        /// Not sealed -- the whole point is that the floor inside is ground. The racking runs
        /// front to back rather than across, because racking across a building is a set of
        /// walls between the door you came in by and the ones you might leave by.
        /// </summary>
        private static void OpenWarehouse(Transform parent, int layer, System.Random rng, Rect footprint, Vector3 front)
        {
            var centre = new Vector3(footprint.center.x, 0f, footprint.center.y);
            bool frontX = Mathf.Abs(front.x) > 0.5f;

            // Local frame: w across the front face, d from the front (0) to the back (depth).
            float width = frontX ? footprint.height : footprint.width;
            float depth = frontX ? footprint.width : footprint.height;
            var across = frontX ? Vector3.forward : Vector3.right;
            var backward = -front;
            var frontFace = centre + front * depth * 0.5f;

            Vector3 P(float w, float y, float d) => frontFace + across * w + backward * d + Vector3.up * y;
            Vector3 S(float w, float y, float d) => frontX ? new Vector3(d, y, w) : new Vector3(w, y, d);

            float eave = Rand(rng, 7.5f, 9f);
            const float thick = 0.35f;

            // ---- walls, each with its opening ----
            var shell = new MeshBuild { UVScale = 0.16f };

            void Wall(Vector3 a, Vector3 b, float doorAt, float doorWidth, float doorHeight)
            {
                // A wall between two corners, in segments round its door.
                var dir = (b - a).normalized;
                float length = Vector3.Distance(a, b);
                bool alongWorldX = Mathf.Abs(dir.x) > 0.5f;

                void Panel(float s0, float s1, float y0, float y1)
                {
                    if (s1 - s0 < 0.05f || y1 - y0 < 0.05f) return;
                    var c = a + dir * ((s0 + s1) * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
                    shell.Box(c, alongWorldX ? new Vector3(s1 - s0, y1 - y0, thick) : new Vector3(thick, y1 - y0, s1 - s0), Quaternion.identity);
                }

                if (doorWidth <= 0f) { Panel(0f, length, 0f, eave); return; }

                float d0 = doorAt - doorWidth * 0.5f, d1 = doorAt + doorWidth * 0.5f;
                Panel(0f, d0, 0f, eave);
                Panel(d1, length, 0f, eave);
                Panel(d0, d1, doorHeight, eave);
            }

            float hw = width * 0.5f;
            float bigDoor = Mathf.Min(8f, width * 0.35f);
            float bigAt = Rand(rng, -hw * 0.3f, hw * 0.3f);
            int sideWithDoor = rng.Next(2) == 0 ? -1 : 1;

            Wall(P(-hw, 0f, 0f), P(hw, 0f, 0f), hw + bigAt, bigDoor, 5.6f);                                  // front
            Wall(P(-hw, 0f, depth), P(hw, 0f, depth), hw + Rand(rng, -hw * 0.4f, hw * 0.4f), 2.6f, 3.1f);      // back
            Wall(P(-hw, 0f, 0f), P(-hw, 0f, depth), sideWithDoor < 0 ? depth * 0.4f : 0f, sideWithDoor < 0 ? 2.6f : 0f, 3.1f);
            Wall(P(hw, 0f, 0f), P(hw, 0f, depth), sideWithDoor > 0 ? depth * 0.4f : 0f, sideWithDoor > 0 ? 2.6f : 0f, 3.1f);

            var cladding = _denseCladding[rng.Next(_denseCladding.Length)];
            var walls = MeshObject(parent, "WarehouseWalls", ToMesh(shell, DenseKey("warehousewalls")), cladding,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(walls);

            // ---- roof ----
            var roof = new MeshBuild { UVScale = 0.2f };
            var gables = new MeshBuild { UVScale = 0.16f };
            GableRoof(roof, gables, centre, footprint.width, footprint.height, eave, underside: true);

            var roofGo = MeshObject(parent, "WarehouseRoof", ToMesh(roof, DenseKey("warehouseroof")), _zonePlate,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(roofGo);
            Hide(roofGo);
            var gableGo = MeshObject(parent, "WarehouseGables", ToMesh(gables, DenseKey("warehousegables")), cladding,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(gableGo);
            Hide(gableGo);

            // Hazard band over the vehicle door.
            var band = new MeshBuild { UVScale = 0.5f };
            band.Box(P(bigAt, 5.95f, -0.1f), S(bigDoor + 0.6f, 0.7f, 0.2f), Quaternion.identity);
            Hide(MeshObject(parent, "WarehouseDoorBand", ToMesh(band, DenseKey("warehouseband")), _zoneHazard,
                            Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));

            // ---- mezzanine along the back wall, up a stair ----
            int steps = 18;
            float mezz = steps * StairRise;
            const float mezzDepth = 5f;
            float stairW = sideWithDoor > 0 ? -hw + 2.2f : hw - 2.2f;   // the side without a door

            var deck = new MeshBuild { UVScale = 0.5f };
            deck.Box(P(0f, mezz - 0.1f, depth - thick - mezzDepth * 0.5f), S(width - thick * 2f, 0.2f, mezzDepth), Quaternion.identity);
            MeshObject(parent, "Mezzanine", ToMesh(deck, DenseKey("mezzdeck")), _zoneDeck,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");

            var frame = new MeshBuild { UVScale = 0.5f };
            float edge = depth - thick - mezzDepth;
            for (float w = -hw + 1.5f; w <= hw - 1.5f; w += 4f)
                frame.Box(P(w, (mezz - 0.2f) * 0.5f, edge + 0.2f), S(0.25f, mezz - 0.2f, 0.25f), Quaternion.identity);

            // Railing along the open edge, with a gap where the stair lands.
            for (float w = -hw + 0.6f; w < hw - 0.6f; w += 0.5f)
            {
                if (Mathf.Abs(w - stairW) < 1.6f) continue;
                frame.Box(P(w + 0.25f, mezz + 1.05f, edge + 0.08f), S(0.5f, 0.07f, 0.07f), Quaternion.identity);
                frame.Box(P(w + 0.25f, mezz + 0.55f, edge + 0.08f), S(0.07f, 1.1f, 0.07f), Quaternion.identity);
            }

            var frameGo = MeshObject(parent, "MezzanineFrame", ToMesh(frame, DenseKey("mezzframe")), _zoneSafety,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(frameGo);
            Hide(frameGo);

            // The stair climbs towards the back and lands on the mezzanine's front edge.
            float yaw = Quaternion.LookRotation(backward).eulerAngles.y;
            BuildStairTower(parent, layer, P(stairW, 0f, edge - StairRun(mezz)), mezz, yaw);

            // ---- racking, in rows from front to back ----
            var racks = new MeshBuild { UVScale = 0.5f };
            var cargo = new MeshBuild { UVScale = 0.6f };
            const float rackDeep = 1.2f, aisle = 3.4f, rackHigh = 4.6f;

            float rowFrom = 3.5f, rowTo = edge - 3f;
            for (float w = -hw + 3.5f; w <= hw - 3.5f; w += rackDeep + aisle)
            {
                // Keep off the stair and the big door's line.
                if (Mathf.Abs(w - stairW) < 3f || Mathf.Abs(w - bigAt) < bigDoor * 0.5f + 1.5f) continue;
                if (rowTo - rowFrom < 4f) break;

                float midD = (rowFrom + rowTo) * 0.5f, len = rowTo - rowFrom;

                for (float d = rowFrom; d <= rowTo + 0.01f; d += len / Mathf.Max(1, Mathf.Round(len / 2.8f)))
                    foreach (float o in new[] { -rackDeep * 0.5f, rackDeep * 0.5f })
                        racks.Box(P(w + o, rackHigh * 0.5f, d), S(0.1f, rackHigh, 0.1f), Quaternion.identity);

                foreach (float y in new[] { 1.5f, 3.0f, 4.5f })
                {
                    foreach (float o in new[] { -rackDeep * 0.5f, rackDeep * 0.5f })
                        racks.Box(P(w + o, y, midD), S(0.1f, 0.14f, len), Quaternion.identity);

                    // Loads on the beams, not on every bay.
                    for (float d = rowFrom + 1.4f; d < rowTo - 1f; d += 2.8f)
                        if (rng.NextDouble() < 0.7)
                            cargo.Box(P(w, y + 0.55f, d), S(rackDeep * 0.9f, 0.95f, 2.2f), Quaternion.identity);
                }

                // Loads on the floor too, which is what makes an aisle something to fight down.
                for (float d = rowFrom + 1.4f; d < rowTo - 1f; d += 2.8f)
                    if (rng.NextDouble() < 0.5)
                        cargo.Box(P(w, 0.5f, d), S(rackDeep * 0.9f, 1f, 2.2f), Quaternion.identity);
            }

            if (racks.Triangles.Count > 0)
            {
                NoStanding(MeshObject(parent, "Racking", ToMesh(racks, DenseKey("racking")), _zoneSafety,
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));
                NoStanding(MeshObject(parent, "RackLoads", ToMesh(cargo, DenseKey("rackloads")), _cargoMat,
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood"));
            }

            // Two lamps, high, so the inside is readable and not a black box.
            for (int k = -1; k <= 1; k += 2)
            {
                var lamp = new GameObject("WarehouseLight");
                lamp.transform.SetParent(parent, false);
                lamp.transform.position = P(k * width * 0.22f, eave - 1f, depth * 0.45f);

                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(width, depth) * 0.7f;
                light.intensity = 1.6f;
                light.color = new Color(1f, 0.95f, 0.85f);
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>A shallow gable over a rectangle, the ridge along its longer side, both slopes and both gable ends.</summary>
        /// <para>
        /// <paramref name="underside"/> adds the slopes again facing down, for a building
        /// somebody stands inside: the upward faces are culled from below, and from inside
        /// a warehouse the roof was simply not there -- open sky over the racking.
        /// </para>
        private static void GableRoof(MeshBuild roof, MeshBuild gables, Vector3 centre, float sx, float sz, float height,
                                      bool underside = false)
        {
            bool ridgeAlongX = sx >= sz;
            float span = ridgeAlongX ? sz : sx, run = ridgeAlongX ? sx : sz;
            float rise = span * 0.14f;

            Vector3 R(float acr, float y, float alg)
                => centre + (ridgeAlongX ? new Vector3(alg, y, acr) : new Vector3(acr, y, alg));

            float e = span * 0.5f + 0.4f, l = run * 0.5f + 0.4f;
            var ridge0 = R(0f, height + rise, -l);
            var ridge1 = R(0f, height + rise, l);

            AddUp(roof, R(-e, height, -l), R(-e, height, l), ridge1, ridge0);
            AddUp(roof, R(e, height, l), R(e, height, -l), ridge0, ridge1);

            if (underside)
            {
                // The same slopes a hair lower, wound the other way.
                var dip = Vector3.down * 0.05f;
                AddDown(roof, R(-e, height, -l) + dip, R(-e, height, l) + dip, ridge1 + dip, ridge0 + dip);
                AddDown(roof, R(e, height, l) + dip, R(e, height, -l) + dip, ridge0 + dip, ridge1 + dip);
            }

            AddOutward(gables, centre, R(-span * 0.5f, height, -run * 0.5f), R(span * 0.5f, height, -run * 0.5f), R(0f, height + rise, -run * 0.5f));
            AddOutward(gables, centre, R(span * 0.5f, height, run * 0.5f), R(-span * 0.5f, height, run * 0.5f), R(0f, height + rise, run * 0.5f));
        }

        // ==================================================================
        // Railway siding
        // ==================================================================
        /// <summary>
        /// A railway siding in the strip between the ring road and the perimeter fence, on two
        /// sides of the site: ballast, sleepers and rails, buffer stops at the ends, and trains
        /// standing on it -- a locomotive and a string of boxcars, tank wagons and flats with
        /// containers on them.
        ///
        /// The strip was the one part of the site with nothing in it, and a siding is exactly
        /// what a works puts there. The trains are cover and a long firing lane, and they stand
        /// in groups with gaps between, because an unbroken train is a wall between the road
        /// and the fence with the only way round a hundred metres off.
        ///
        /// The track is on the layer the bake ignores and has no collider: rails fifteen
        /// centimetres high are something to step over, not an obstacle. The wagons are held
        /// off the bake whole -- a boxcar is a closed box standing on a chassis, and the
        /// chassis deck inside it would otherwise bake as a room.
        /// </summary>
        private static void BuildRailway(Transform root, int layer, int backdrop, System.Random rng, float half)
        {
            var group = new GameObject("Railway").transform;
            group.SetParent(root, false);

            // Between the ring road's outer kerb and the fence: see PlanStreets for the ring.
            int ringTile = Mathf.RoundToInt(_theme.arenaSize / Tile) / 2 - 1;
            float offset = ringTile * Tile + CarriageHalf + 8f;

            // Each line starts just past the corner the two share, so they never cross.
            float aMin = -offset + 8f, aMax = half - 10f;
            float length = aMax - aMin, aMid = (aMin + aMax) * 0.5f;

            var ballast = new MeshBuild { UVScale = 0.3f };
            var sleepers = new MeshBuild { UVScale = 0.5f };
            var rails = new MeshBuild { UVScale = 0.5f };
            const float gauge = 1.435f;

            // South and west.
            foreach (int line in new[] { 0, 1 })
            {
                bool alongX = line == 0;
                Vector3 P(float a, float y, float o) => alongX ? new Vector3(a, y, -offset + o) : new Vector3(-offset + o, y, a);
                Vector3 S(float a, float y, float o) => alongX ? new Vector3(a, y, o) : new Vector3(o, y, a);

                ballast.Box(P(aMid, 0.03f, 0f), S(length, 0.06f, 3.6f), Quaternion.identity);

                for (float a = aMin + 0.4f; a < aMax; a += 0.65f)
                    sleepers.Box(P(a, 0.1f, 0f), S(0.24f, 0.12f, 2.6f), Quaternion.identity);

                foreach (float side in new[] { -gauge * 0.5f, gauge * 0.5f })
                    rails.Box(P(aMid, 0.24f, side), S(length, 0.16f, 0.09f), Quaternion.identity);

                // Buffer stops at both ends.
                var stops = new MeshBuild { UVScale = 0.5f };
                foreach (float end in new[] { -1f, 1f })
                {
                    float a = end < 0f ? aMin + 1f : aMax - 1f;
                    stops.Box(P(a, 0.8f, 0f), S(1.2f, 1.6f, 3f), Quaternion.identity);
                    stops.Box(P(a - end * 0.7f, 1.1f, 0f), S(0.3f, 0.5f, 2.6f), Quaternion.identity);
                }
                NoStanding(MeshObject(group, "BufferStops", ToMesh(stops, DenseKey("bufferstops")), _zoneHazard,
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));

                // ---- the trains ----
                float cursor = aMin + Rand(rng, 12f, 30f);
                int groups = 0;

                while (cursor < aMax - 30f && groups < 3)
                {
                    int wagons = 3 + rng.Next(3);
                    bool loco = groups == 0 || rng.NextDouble() < 0.5;

                    if (loco)
                    {
                        Locomotive(group, layer, P(cursor + 9f, 0f, 0f), alongX);
                        cursor += 18f + 1.4f;
                    }

                    for (int w = 0; w < wagons && cursor < aMax - 18f; w++)
                    {
                        int kind = rng.Next(4);
                        float len = kind == 2 ? 13f : 14f;
                        Wagon(group, layer, rng, P(cursor + len * 0.5f, 0f, 0f), alongX, len, kind);
                        cursor += len + 1.4f;
                    }

                    // A clear gap before the next group, wide enough to walk through.
                    cursor += Rand(rng, 14f, 30f);
                    groups++;
                }
            }

            Flat(group, backdrop, "Ballast", ballast, DenseKey("ballast"), _ballastMat);
            Flat(group, backdrop, "Sleepers", sleepers, DenseKey("sleepers"), _sleeperMat);
            Flat(group, backdrop, "Rails", rails, DenseKey("rails"), _zoneSteel);
        }

        /// <summary>
        /// The running gear every wagon shares: a frame, two bogies and their wheels, and a
        /// buffer at each end. Built into the wagon's own mesh.
        /// </summary>
        private static void Chassis(MeshBuild build, float length)
        {
            build.Box(new Vector3(0f, 1.05f, 0f), new Vector3(2.6f, 0.35f, length), Quaternion.identity);

            foreach (float end in new[] { -1f, 1f })
            {
                float z = end * (length * 0.5f - 2.4f);
                build.Box(new Vector3(0f, 0.62f, z), new Vector3(2.2f, 0.45f, 2.6f), Quaternion.identity);

                foreach (float axle in new[] { -0.9f, 0.9f })
                    foreach (float side in new[] { -0.72f, 0.72f })
                        build.Tube(new Vector3(side - 0.1f, 0.46f, z + axle), new Vector3(side + 0.1f, 0.46f, z + axle), 0.46f, 0.46f, 10);

                foreach (float side in new[] { -0.8f, 0.8f })
                    build.Box(new Vector3(side, 1.05f, end * (length * 0.5f + 0.25f)), new Vector3(0.35f, 0.35f, 0.5f), Quaternion.identity);
            }
        }

        /// <summary>A wagon: 0 a boxcar, 1 a tank wagon, 2 a flat with containers, 3 an open hopper.</summary>
        private static void Wagon(Transform parent, int layer, System.Random rng, Vector3 at, bool alongX, float length, int kind)
        {
            var build = new MeshBuild { UVScale = 0.25f };
            Chassis(build, length);

            Material body;

            switch (kind)
            {
                case 0:
                    build.Box(new Vector3(0f, 2.9f, 0f), new Vector3(2.9f, 3.3f, length - 0.4f), Quaternion.identity);
                    build.Box(new Vector3(0f, 4.62f, 0f), new Vector3(2.7f, 0.16f, length - 0.6f), Quaternion.identity);
                    // Sliding door, proud of the side.
                    foreach (float side in new[] { -1.5f, 1.5f })
                        build.Box(new Vector3(side, 2.7f, 0f), new Vector3(0.08f, 2.6f, 3f), Quaternion.identity);
                    body = _denseCladding[4];
                    break;

                case 1:
                    build.Tube(new Vector3(0f, 2.65f, -length * 0.5f + 0.6f), new Vector3(0f, 2.65f, length * 0.5f - 0.6f), 1.35f, 1.35f, 16);
                    build.Tube(new Vector3(0f, 3.9f, -0.5f), new Vector3(0f, 4.3f, -0.5f), 0.45f, 0.4f, 10);
                    body = _zonePlate;
                    break;

                case 2:
                    body = _zoneRust;
                    break;

                default:
                    // Open hopper: sloped ends, open top.
                    build.Box(new Vector3(0f, 2.5f, 0f), new Vector3(2.9f, 2.5f, length - 2.2f), Quaternion.identity);
                    foreach (float end in new[] { -1f, 1f })
                        build.Box(new Vector3(0f, 2.1f, end * (length * 0.5f - 1.3f)), new Vector3(2.9f, 1.7f, 1.6f), Quaternion.Euler(end * 25f, 0f, 0f));
                    body = _denseCladding[2];
                    break;
            }

            var go = MeshObject(parent, "Wagon", ToMesh(build, DenseKey("wagon")), body,
                                at, Quaternion.Euler(0f, alongX ? 90f : 0f, 0f), Vector3.one, layer, "Metal");
            NoStanding(go);

            if (kind == 2 && go != null)
            {
                // Two twenty-foot boxes on the flat, from the pack.
                foreach (float z in new[] { -3.2f, 3.2f })
                {
                    var box = Place(go.transform, BoxClosed, new Vector3(0f, 1.23f, z), 90f);
                    NoStanding(box);
                }
            }
        }

        private static void Locomotive(Transform parent, int layer, Vector3 at, bool alongX)
        {
            const float length = 18f;
            var build = new MeshBuild { UVScale = 0.25f };
            Chassis(build, length);

            // Long hood, cab at one end, short hood beyond it.
            build.Box(new Vector3(0f, 2.55f, -2f), new Vector3(2.3f, 2.6f, 11.5f), Quaternion.identity);
            build.Box(new Vector3(0f, 2.95f, 5.6f), new Vector3(2.95f, 3.4f, 3.4f), Quaternion.identity);
            build.Box(new Vector3(0f, 2.3f, 8.2f), new Vector3(2.3f, 2.1f, 1.6f), Quaternion.identity);
            build.Box(new Vector3(0f, 4.0f, -5f), new Vector3(1.2f, 0.5f, 3f), Quaternion.identity);

            var go = MeshObject(parent, "Locomotive", ToMesh(build, DenseKey("loco")), _locoMat,
                                at, Quaternion.Euler(0f, alongX ? 90f : 0f, 0f), Vector3.one, layer, "Metal");
            NoStanding(go);

            if (go != null)
            {
                var glass = new MeshBuild { UVScale = 0.5f };
                foreach (float side in new[] { -1.49f, 1.49f })
                    glass.Box(new Vector3(side, 3.7f, 5.6f), new Vector3(0.06f, 1f, 2.2f), Quaternion.identity);
                glass.Box(new Vector3(0f, 3.7f, 7.31f), new Vector3(2.4f, 1f, 0.06f), Quaternion.identity);
                Hide(MeshObject(go.transform, "CabWindows", ToMesh(glass, DenseKey("locoglass")), _denseGlass,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));
            }
        }

        // ==================================================================
        // Conveyors
        // ==================================================================
        /// <summary>
        /// A running belt up to the headhouse of every silo bank that has the ground for one:
        /// an inclined conveyor on legs, its belt scrolling, feeding the bank from a hopper at
        /// ground level. It is what a silo bank is <i>for</i>, and without it the bank was
        /// four tins in a row.
        ///
        /// The belt carries its own UVs -- across it and along it -- rather than the planar
        /// projection every other mesh here uses, so the scroll runs along the belt whichever
        /// way the bank is turned.
        /// </summary>
        private static void BuildSiloConveyors(Transform root, int layer, System.Random rng)
        {
            var banks = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == "SiloBank") banks.Add(t.gameObject);

            foreach (var bank in banks)
            {
                var filter = bank.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                var b = filter.sharedMesh.bounds;
                float headTop = b.max.y - 8.3f + 2.8f;

                // Out of the end of the headhouse, away from the elevator leg.
                var top = bank.transform.TransformPoint(new Vector3(b.max.x - 1f, headTop - 1.4f, 0f));
                var outward = bank.transform.right;

                const float incline = 32f;
                float rise = top.y - 1.2f;
                float run = rise / Mathf.Tan(incline * Mathf.Deg2Rad);
                var foot = top + outward * run;
                foot.y = 1.2f;

                // The whole footprint has to be clear up to the belt's height at each point,
                // which a box to the full rise is a safe stand-in for.
                var lo = Vector3.Min(top, foot) - new Vector3(1.8f, 0f, 1.8f);
                var hi = Vector3.Max(top, foot) + new Vector3(1.8f, 0f, 1.8f);
                var area = new Rect(lo.x, lo.z, hi.x - lo.x, hi.z - lo.z);
                var near = Grow(new Rect(foot.x - 3f, foot.z - 3f, 6f, 6f), 2f);

                if (!GroundFree(near, 4f)) continue;
                if (!GroundFree(Grow(area, -Mathf.Min(area.width, area.height) * 0.1f), 2.5f)) continue;

                BuildConveyor(root, layer, rng, foot, top);
            }
        }

        private static void BuildConveyor(Transform parent, int layer, System.Random rng, Vector3 foot, Vector3 top)
        {
            var dir = top - foot;
            float length = dir.magnitude;
            dir /= length;

            var side = Vector3.Cross(Vector3.up, dir).normalized;
            const float beltHalf = 0.55f;

            // ---- frame: channels either side, legs to the ground, a hopper at the foot ----
            var frame = new MeshBuild { UVScale = 0.5f };
            var rotation = Quaternion.LookRotation(dir, Vector3.up);

            foreach (float s in new[] { -1f, 1f })
                frame.Box((foot + top) * 0.5f + side * s * (beltHalf + 0.15f), new Vector3(0.12f, 0.35f, length), rotation);

            int legs = Mathf.Max(2, Mathf.RoundToInt(length / 7f));
            for (int i = 1; i <= legs; i++)
            {
                var p = Vector3.Lerp(foot, top, i / (float)(legs + 1));
                foreach (float s in new[] { -1f, 1f })
                {
                    var at = p + side * s * (beltHalf + 0.15f);
                    frame.Box(new Vector3(at.x, at.y * 0.5f, at.z), new Vector3(0.18f, at.y, 0.18f), Quaternion.identity);
                }
            }

            frame.Box(foot + Vector3.down * 0.3f - dir * 0.8f, new Vector3(2.6f, 1.6f, 2.6f), Quaternion.identity);

            var frameGo = MeshObject(parent, "Conveyor", ToMesh(frame, DenseKey("conveyor")), _zoneSafety,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(frameGo);
            Hide(frameGo);

            // ---- the belt, with its own UVs so the scroll runs along it ----
            var mesh = new Mesh { name = "Belt" };
            var lift = Vector3.up * 0.12f;
            mesh.vertices = new[]
            {
                foot - side * beltHalf + lift, foot + side * beltHalf + lift,
                top - side * beltHalf + lift, top + side * beltHalf + lift
            };
            // One repeat of the belt map every four metres: eight cleats, half a metre apart.
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, length / 4f), new Vector2(1f, length / 4f) };

            var up = Vector3.Cross(dir, side);
            mesh.triangles = Vector3.Dot(Vector3.Cross(mesh.vertices[2] - mesh.vertices[0], mesh.vertices[1] - mesh.vertices[0]), up) > 0f
                ? new[] { 0, 2, 1, 1, 2, 3 }
                : new[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var belt = MeshObject(parent, "ConveyorBelt", mesh, _beltMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, null, collider: false);
            Hide(belt);

            var scroll = belt.AddComponent<ScrollingWater>();
            // A quarter of a repeat a second is a metre a second, up the belt.
            scroll.scrollSpeed = new Vector2(0f, -Rand(rng, 0.22f, 0.3f));
        }

        // ==================================================================
        // Fans
        // ==================================================================
        /// <summary>
        /// An extract fan on a roof: a short drum, and a hub with blades that turns. Called
        /// for the tall process buildings, whose rooftop plant was the stillest thing on them.
        /// </summary>
        private static void RoofFan(Transform parent, int layer, System.Random rng, Vector3 at, float radius)
        {
            var drum = new MeshBuild { UVScale = 0.5f };
            drum.Tube(at, at + Vector3.up * 1.4f, radius, radius, 16);
            drum.Tube(at + Vector3.up * 1.4f, at + Vector3.up * 1.6f, radius + 0.12f, radius + 0.12f, 16);
            NoStanding(MeshObject(parent, "FanDrum", ToMesh(drum, DenseKey("fandrum")), _zonePlate,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));

            var blades = new MeshBuild { UVScale = 0.5f };
            blades.Tube(new Vector3(0f, -0.1f, 0f), new Vector3(0f, 0.25f, 0f), radius * 0.18f, radius * 0.18f, 10);
            int count = 5;
            for (int i = 0; i < count; i++)
                blades.Box(new Vector3(0f, 0f, 0f) + Quaternion.Euler(0f, i * 360f / count, 0f) * new Vector3(radius * 0.5f, 0f, 0f),
                           new Vector3(radius * 0.85f, 0.05f, radius * 0.32f),
                           Quaternion.Euler(0f, i * 360f / count, 0f) * Quaternion.Euler(18f, 0f, 0f));

            var fan = MeshObject(parent, "FanBlades", ToMesh(blades, DenseKey("fanblades")), _lampPole,
                                 at + Vector3.up * 1.35f, Quaternion.identity, Vector3.one, layer, null, collider: false);
            Hide(fan);

            var spin = fan.AddComponent<MachineSpin>();
            spin.rest = Quaternion.identity;
            spin.axis = Vector3.up;
            spin.degreesPerSecond = Rand(rng, 180f, 320f);
            spin.phase = Rand(rng, 0f, 360f);
        }
    }
}
#endif
