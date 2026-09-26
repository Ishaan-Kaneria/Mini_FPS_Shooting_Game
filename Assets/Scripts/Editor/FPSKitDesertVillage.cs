#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The village: the box town made into somewhere people live. Ishaan asked for it by
    /// name -- "real desert village" -- over keeping the grid of plain boxes it was.
    ///
    /// What makes a mud-brick house read as one rather than as a block is almost all at
    /// the edges: a darker plinth where the wall meets the dirt, a door that is painted and
    /// framed with a timber lintel, windows with shutters or a grille, beam ends through
    /// the wall, a parapet, and on the roof the water tank, the dish and the washing. So
    /// every house here gets those, whatever else it is.
    ///
    /// <b>A third of them can be gone into.</b> Walls with real openings, two rooms with a
    /// doorway between, furniture, a door out front and another out back so no room is a
    /// dead end, and a stair up the outside to a roof that is ground. The rest are solid
    /// and sealed off the bake, as the town's houses always were.
    ///
    /// Each lot is its own <see cref="Frame"/>, turned by a random quarter so the street
    /// is not every house facing the same way; lots are square, so any turn fits.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float VillageStorey = 18 * StairRise;   // a whole number of risers, for the stair

        /// <summary>
        /// The village on its flattened site: lots on a grid of lanes, the market square in
        /// the middle, a mosque and its minaret by the square, pickups along the main street,
        /// poles and wires down it.
        /// </summary>
        private static void BuildVillage(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var town = new GameObject("Town").transform;
            town.SetParent(parent, false);

            var c = _townCentre;
            float floor = GroundHeightAt(c.x, c.y);
            float R = _townRadius;

            // Packed earth under the whole village, so the lanes read as lanes.
            var earth = new MeshBuild { UVScale = 0.2f };
            const int Sides = 40;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                float r0 = (R - 3f) * (1f + Fbm2(Mathf.Cos(a0) * 2f, Mathf.Sin(a0) * 2f, 7201, 2) * 0.12f);
                float r1 = (R - 3f) * (1f + Fbm2(Mathf.Cos(a1) * 2f, Mathf.Sin(a1) * 2f, 7201, 2) * 0.12f);
                AddUp(earth, new Vector3(c.x, floor + 0.03f, c.y), new Vector3(c.x + Mathf.Cos(a0) * r0, floor + 0.03f, c.y + Mathf.Sin(a0) * r0),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, floor + 0.03f, c.y + Mathf.Sin(a1) * r1),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, floor + 0.03f, c.y + Mathf.Sin(a1) * r1));
            }
            Flat(town, backdrop, "TownGround", earth, DenseKey("townground"), _earthMat);

            // ---- lots on a grid of lanes ----
            const float lot = 11f, lane = 4f, pitch = lot + lane;
            const float mainHalf = 4.5f, crossHalf = 3f;
            float plazaX = crossHalf + lot, plazaZ = mainHalf + lot;

            var lots = new List<Vector2>();
            for (int j = -6; j <= 6; j++)
                for (int k = -6; k <= 6; k++)
                {
                    if (j == 0 || k == 0) continue;
                    float x = c.x + Mathf.Sign(j) * (crossHalf + lot * 0.5f + (Mathf.Abs(j) - 1) * pitch);
                    float z = c.y + Mathf.Sign(k) * (mainHalf + lot * 0.5f + (Mathf.Abs(k) - 1) * pitch);

                    var p = new Vector2(x, z);
                    if ((p - c).magnitude + lot * 0.7f > R - 3f) continue;
                    if (Mathf.Abs(x - c.x) < plazaX && Mathf.Abs(z - c.y) < plazaZ) continue;
                    lots.Add(p);
                }

            // The minaret takes the lot nearest a corner of the square, the mosque the lot
            // beside it along the main street.
            Vector2 minaretLot = lots.Count > 0 ? lots[0] : c, mosqueLot = minaretLot;
            float nearest = float.MaxValue;
            var corner = c + new Vector2(plazaX, plazaZ);
            foreach (var p in lots) { float d = (p - corner).magnitude; if (d < nearest) { nearest = d; minaretLot = p; } }
            nearest = float.MaxValue;
            foreach (var p in lots)
            {
                if (p == minaretLot || Mathf.Abs(p.y - minaretLot.y) > 1f) continue;
                float d = (p - minaretLot).magnitude;
                if (d < nearest) { nearest = d; mosqueLot = p; }
            }

            int enterable = 0, solid = 0, yards = 0, ruins = 0, empty = 0;
            foreach (var p in lots)
            {
                var at = new Vector3(p.x, floor, p.y);
                if (p == minaretLot) { BuildMinaret(town, layer, at); continue; }
                if (p == mosqueLot && mosqueLot != minaretLot) { BuildMosque(town, layer, new Frame(at, (p.y > c.y ? 0f : 180f))); continue; }

                var f = new Frame(at, rng.Next(4) * 90f);
                double roll = rng.NextDouble();
                if (roll < 0.07) { EmptyLot(town, layer, backdrop, rng, f, lot); empty++; continue; }
                if (roll < 0.17) { BuildRuin(town, layer, rng, at, lot); ruins++; continue; }
                if (roll < 0.50) { BuildEnterableHouse(town, layer, backdrop, rng, f, lot); enterable++; continue; }

                bool yard = rng.NextDouble() < 0.4;
                BuildSolidHouse(town, layer, rng, f, lot, yard);
                if (yard) yards++;
                solid++;
            }

            BuildMarket(town, layer, rng, new Vector3(c.x, floor, c.y), plazaX, plazaZ);

            // Poles and wires down the main street, on the lane corners so no house is in the way.
            var poles = new List<Vector3>();
            for (int j = -6; j <= 6; j++)
            {
                if (j == 0) continue;
                float x = c.x + Mathf.Sign(j) * (crossHalf + lot + lane * 0.5f + (Mathf.Abs(j) - 1) * pitch);
                if (Mathf.Abs(x - c.x) > R - 6f) continue;
                poles.Add(new Vector3(x, floor, c.y + mainHalf - 0.4f));
            }
            poles.Sort((a, b) => a.x.CompareTo(b.x));
            BuildPoleRun(town, layer, backdrop, poles, 7.5f);

            // Pickups parked along the main street, off the crown where the track runs.
            int parked = 0;
            for (int i = 0; i < 30 && parked < 7; i++)
            {
                float x = c.x + Rand(rng, -R + 10f, R - 10f);
                if (Mathf.Abs(x - c.x) < plazaX + 2f) continue;
                float side = rng.Next(2) == 0 ? -1f : 1f;
                var at = new Vector2(x, c.y + side * 3.1f);
                if (poles.Exists(q => Mathf.Abs(q.x - x) < 3.4f)) continue;
                BuildVehicle(town, layer, rng, at, 90f + (side > 0 ? 0f : 180f) + Rand(rng, -4f, 4f), VehicleKind.Pickup,
                             _carPaints[rng.Next(_carPaints.Length)]);
                parked++;
            }

            // Palms at the edge of the village and in the square.
            for (int i = 0; i < 10; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                var at = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(rng, R + 2f, R + 12f);
                if (!Free(at, 2f)) continue;
                BuildPalm(town, layer, rng, new Vector3(at.x, GroundHeightAt(at.x, at.y) - 0.2f, at.y));
            }

            Debug.Log($"[FPSKit] desert village: {enterable} enterable house(s), {solid} solid ({yards} with a yard), " +
                      $"{ruins} ruin(s), {empty} empty lot(s), {parked} pickup(s).");
        }

        // ==================================================================
        // Houses
        // ==================================================================
        /// <summary>
        /// A solid house on its lot, sealed off the bake: one storey or two, dressed. With a
        /// yard, the house takes the back of the lot and a wall closes the front, with a
        /// gate in the front and another in a side, so the yard is a way through as well as
        /// somewhere to fight from.
        /// </summary>
        private static void BuildSolidHouse(Transform parent, int layer, System.Random rng, Frame f, float lot, bool yard)
        {
            float storey = VillageStorey;
            bool twoStorey = rng.NextDouble() < 0.35;
            var mat = _adobeTints[rng.Next(_adobeTints.Length)];

            float w, d, cu, cv;
            if (yard)
            {
                w = lot - 0.6f;
                d = Rand(rng, 5.2f, 6.2f);
                cu = 0f;
                cv = lot * 0.5f - 0.3f - d * 0.5f;
            }
            else
            {
                w = Rand(rng, 7.5f, lot - 0.6f);
                d = Rand(rng, 7.5f, lot - 0.6f);
                cu = Rand(rng, -(lot - w) * 0.5f, (lot - w) * 0.5f);
                cv = Rand(rng, -(lot - d) * 0.5f, (lot - d) * 0.5f);
            }

            var shell = new MeshBuild { UVScale = 0.28f };
            shell.Box(f.P(cu, storey * 0.5f, cv), new Vector3(w, storey, d), f.Rot);

            float top = storey;
            float w2 = 0f, d2 = 0f, u2 = 0f, v2 = 0f;
            if (twoStorey)
            {
                w2 = w * Rand(rng, 0.55f, 0.8f);
                d2 = d * Rand(rng, 0.6f, 0.85f);
                u2 = cu + (w - w2) * 0.5f * (rng.Next(2) == 0 ? 1 : -1);
                v2 = cv + (d - d2) * 0.5f;
                shell.Box(f.P(u2, storey + 1.6f, v2), new Vector3(w2, 3.2f, d2), f.Rot);
                top = storey + 3.2f;
            }

            var house = MeshObject(parent, "House", ToMesh(shell, DenseKey("house")), mat, Vector3.zero, Quaternion.identity,
                                   Vector3.one, layer, "Concrete");
            NoStanding(house);
            SealLocal(parent, f, cu, top * 0.5f, cv, new Vector3(w, top, d));

            Parapet(parent, layer, f, mat, cu, cv, w, d, storey, float.NaN);
            if (twoStorey) Parapet(parent, layer, f, mat, u2, v2, w2, d2, top, float.NaN);

            // Doors: the front always (towards the yard, if there is one), sometimes a side.
            var doors = new List<(int face, float at)> { (0, Rand(rng, -w * 0.3f, w * 0.3f)) };
            if (!yard && rng.NextDouble() < 0.5) doors.Add((2, Rand(rng, -d * 0.25f, d * 0.25f)));
            HouseDressing(parent, layer, rng, f, cu, cv, w, d, storey, doors, solidDoors: true);
            if (twoStorey) UpperWindows(parent, layer, rng, f, u2, v2, w2, d2, storey);
            RoofDressing(parent, layer, rng, f, twoStorey ? u2 : cu, twoStorey ? v2 : cv, twoStorey ? w2 : w, twoStorey ? d2 : d,
                         top, walkable: false);

            if (yard) YardWall(parent, layer, rng, f, lot, cv - d * 0.5f, mat);
            else LotClutter(parent, layer, rng, f, lot, cu, cv, w, d);
        }

        /// <summary>
        /// A house that can be gone into: walls with real doors and windows, two rooms, a
        /// doorway between them, furniture, and a stair up the +u side to the roof. The roof
        /// is ground and nothing about the house is sealed -- the floor inside is the lot.
        /// </summary>
        private static void BuildEnterableHouse(Transform parent, int layer, int backdrop, System.Random rng, Frame f, float lot)
        {
            float storey = VillageStorey;
            const float t = 0.35f;
            var mat = _adobeTints[rng.Next(_adobeTints.Length)];

            float w = Rand(rng, 7f, 7.6f), d = Rand(rng, 8.8f, lot - 0.6f);
            float cu = -(lot - w) * 0.5f + 0.25f, cv = 0f;
            float hw = w * 0.5f, hd = d * 0.5f;
            float gapV = cv + hd - 1.6f;

            // ---- walls, with openings ----
            var walls = new MeshBuild { UVScale = 0.28f };
            float frontDoor = Rand(rng, -hw * 0.35f, hw * 0.35f);
            float backDoor = Rand(rng, -hw * 0.35f, hw * 0.35f);
            float inner = Mathf.Max(0.3f, hw - t);

            var front = new List<Opening> { new Opening(hw + frontDoor, 1.6f, 0f, 2.3f) };
            float fw = frontDoor > 0f ? hw - hw * 0.6f : hw + hw * 0.6f;
            front.Add(new Opening(fw, 1.0f, 1.1f, 2.1f));

            var back = new List<Opening> { new Opening(hw + backDoor, 1.6f, 0f, 2.3f) };
            var left = new List<Opening>
            {
                new Opening(hd - t - d * 0.25f, 1.0f, 1.1f, 2.1f),
                new Opening(hd - t + d * 0.25f, 1.0f, 1.1f, 2.1f)
            };

            // Local wall runs, each from one corner to the next; the -u wall and the two long
            // walls carry openings, the +u wall is where the stair climbs and stays blank.
            WallRun(walls, f, new Vector2(cu - hw, cv - hd + t * 0.5f), new Vector2(cu + hw, cv - hd + t * 0.5f), 0f, storey, t, front);
            WallRun(walls, f, new Vector2(cu - hw, cv + hd - t * 0.5f), new Vector2(cu + hw, cv + hd - t * 0.5f), 0f, storey, t, back);
            WallRun(walls, f, new Vector2(cu - hw + t * 0.5f, cv - hd + t), new Vector2(cu - hw + t * 0.5f, cv + hd - t), 0f, storey, t, left);
            WallRun(walls, f, new Vector2(cu + hw - t * 0.5f, cv - hd + t), new Vector2(cu + hw - t * 0.5f, cv + hd - t), 0f, storey, t);

            // The partition, across u, with a doorway kept off the line of either outside door.
            float pv = cv + Rand(rng, -0.8f, 0.8f);
            float pDoor = Mathf.Abs(frontDoor) > hw * 0.15f ? -frontDoor : (rng.Next(2) == 0 ? -1f : 1f) * hw * 0.4f;
            WallRun(walls, f, new Vector2(cu - hw + t, pv), new Vector2(cu + hw - t, pv), 0f, storey - 0.3f, 0.2f,
                    new List<Opening> { new Opening(inner + pDoor, 1.6f, 0f, 2.3f) });

            var wallGo = MeshObject(parent, "HouseWalls", ToMesh(walls, DenseKey("housewalls")), mat, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(wallGo);

            // ---- the roof, which is ground ----
            var roof = new MeshBuild { UVScale = 0.28f };
            roof.Box(f.P(cu, storey - 0.15f, cv), new Vector3(w, 0.3f, d), f.Rot);
            MeshObject(parent, "HouseRoof", ToMesh(roof, DenseKey("houseroof")), mat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Concrete");

            Parapet(parent, layer, f, mat, cu, cv, w, d, storey, gapV);

            // ---- inside ----
            var floorBuild = new MeshBuild { UVScale = 0.5f };
            AddUp(floorBuild, f.P(cu - hw + t, 0.02f, cv - hd + t), f.P(cu - hw + t, 0.02f, cv + hd - t),
                  f.P(cu + hw - t, 0.02f, cv + hd - t), f.P(cu + hw - t, 0.02f, cv - hd + t));
            Flat(parent, backdrop, "HouseFloor", floorBuild, DenseKey("housefloor"), _plinthMat);

            Furnish(parent, layer, backdrop, rng, f, cu, cv - hd + t, cu + hw - t, pv - 0.1f, cu - hw + t, frontDoor + cu, pDoor + cu);
            Furnish(parent, layer, backdrop, rng, f, cu, pv + 0.1f, cu + hw - t, cv + hd - t, cu - hw + t, backDoor + cu, pDoor + cu);

            // ---- outside ----
            var doors = new List<(int face, float at)> { (0, frontDoor), (1, backDoor) };
            HouseDressing(parent, layer, rng, f, cu, cv, w, d, storey, doors, solidDoors: false, openWindowsOn: new[] { 0, 2, 3 });
            RoofDressing(parent, layer, rng, f, cu - 0.6f, cv - 0.8f, w - 2.4f, d - 3.6f, storey, walkable: true);

            // The stair: along the +u side, climbing towards +v, landing beside the parapet's gap.
            StairLocal(parent, layer, mat, f, cu + hw + 0.95f, gapV, Vector3.forward, storey);
        }

        /// <summary>
        /// A parapet round a roof at <paramref name="roofY"/>, broken where a stair lands
        /// (<paramref name="gapV"/>, on the +u edge; NaN for none). Drain spouts through it.
        /// </summary>
        private static void Parapet(Transform parent, int layer, Frame f, Material mat, float cu, float cv, float w, float d,
                                    float roofY, float gapV)
        {
            var parapet = new MeshBuild { UVScale = 0.3f };
            const float ph = 0.8f, pt = 0.28f;
            float y = roofY + ph * 0.5f;
            float hw = w * 0.5f, hd = d * 0.5f;

            parapet.Box(f.P(cu, y, cv - hd + pt * 0.5f), new Vector3(w, ph, pt), f.Rot);
            parapet.Box(f.P(cu, y, cv + hd - pt * 0.5f), new Vector3(w, ph, pt), f.Rot);
            parapet.Box(f.P(cu - hw + pt * 0.5f, y, cv), new Vector3(pt, ph, d - pt * 2f), f.Rot);

            float eu = cu + hw - pt * 0.5f;
            if (!float.IsNaN(gapV))
            {
                float z0 = cv - hd + pt, z1 = gapV - 1.2f, z2 = gapV + 1.2f, z3 = cv + hd - pt;
                if (z1 > z0) parapet.Box(f.P(eu, y, (z0 + z1) * 0.5f), new Vector3(pt, ph, z1 - z0), f.Rot);
                if (z3 > z2) parapet.Box(f.P(eu, y, (z2 + z3) * 0.5f), new Vector3(pt, ph, z3 - z2), f.Rot);
            }
            else parapet.Box(f.P(eu, y, cv), new Vector3(pt, ph, d - pt * 2f), f.Rot);

            // A coping course a hand's width proud: what makes a parapet a finished edge.
            var coping = new MeshBuild { UVScale = 0.3f };
            coping.Box(f.P(cu, roofY + ph + 0.04f, cv - hd + pt * 0.5f), new Vector3(w + 0.1f, 0.08f, pt + 0.1f), f.Rot);
            coping.Box(f.P(cu, roofY + ph + 0.04f, cv + hd - pt * 0.5f), new Vector3(w + 0.1f, 0.08f, pt + 0.1f), f.Rot);

            Solid(parent, "Parapet", parapet, mat, layer);
            Visual(parent, "Coping", coping, _plinthMat, layer);

            // Spouts: a short pipe out through the parapet's foot on the long faces.
            var spouts = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -1f, 1f })
            {
                float at = cu + (s > 0 ? hw * 0.5f : -hw * 0.4f);
                var root = f.P(at, roofY + 0.1f, cv + s * hd);
                spouts.Tube(root, root + f.Axis(new Vector3(0f, -0.05f, s * 0.55f)), 0.07f, 0.06f, 6);
            }
            Visual(parent, "Spouts", spouts, _timberMat, layer);
        }

        /// <summary>
        /// The dressing that makes a wall a house: plinth, beam ends, doors (painted leaves
        /// on a solid house, framed openings on an open one), windows with shutters or
        /// grilles, an air-conditioner on a wall. Faces: 0 is -v (front), 1 +v, 2 -u, 3 +u.
        /// </summary>
        private static void HouseDressing(Transform parent, int layer, System.Random rng, Frame f, float cu, float cv,
                                          float w, float d, float storey, List<(int face, float at)> doors, bool solidDoors,
                                          int[] openWindowsOn = null)
        {
            float hw = w * 0.5f, hd = d * 0.5f;

            // A strip down each face, not a box under the house: a box would fill the rooms of
            // an open house to shin height and close its doorways to the bake.
            var plinth = new MeshBuild { UVScale = 0.3f };

            var beams = new MeshBuild { UVScale = 0.5f };
            var frames = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var paint = new MeshBuild { UVScale = 0.5f };
            var grilles = new MeshBuild { UVScale = 0.5f };

            // Where a face is, and which way it faces, in local terms.
            (Vector2 centre, Vector2 along, Vector2 outward, float length) Face(int face) => face switch
            {
                0 => (new Vector2(cu, cv - hd), Vector2.right, Vector2.down, w),
                1 => (new Vector2(cu, cv + hd), Vector2.right, Vector2.up, w),
                2 => (new Vector2(cu - hw, cv), Vector2.up, Vector2.left, d),
                _ => (new Vector2(cu + hw, cv), Vector2.up, Vector2.right, d)
            };

            Quaternion FaceRot(Vector2 along) => f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-along.y, along.x) * Mathf.Rad2Deg, 0f);

            for (int face = 0; face < 4; face++)
            {
                var (centre, along, outward, length) = Face(face);
                var rot = FaceRot(along);
                Vector3 On(float s, float y, float out_) { var p = centre + along * s + outward * out_; return f.P(p.x, y, p.y); }

                // Beam ends under the roof on the long faces.
                if (face < 2)
                    for (float s = -length * 0.5f + 0.8f; s < length * 0.5f - 0.6f; s += 1.3f)
                        beams.Box(On(s, storey - 0.45f, 0.25f), new Vector3(0.18f, 0.18f, 0.5f), rot);

                var doorsHere = doors.FindAll(x => x.face == face);

                float from = -length * 0.5f - 0.04f;
                var cuts = new List<float>();
                foreach (var door in doorsHere) { cuts.Add(door.at - 0.8f); cuts.Add(door.at + 0.8f); }
                cuts.Sort();
                for (int k = 0; k <= cuts.Count; k += 2)
                {
                    float to = k < cuts.Count ? cuts[k] : length * 0.5f + 0.04f;
                    if (to - from > 0.05f) plinth.Box(On((from + to) * 0.5f, 0.28f, 0.03f), new Vector3(to - from, 0.56f, 0.06f), rot);
                    if (k + 1 < cuts.Count) from = cuts[k + 1];
                }
                foreach (var door in doorsHere)
                {
                    // A timber lintel over the door, and jambs either side.
                    frames.Box(On(door.at, 2.42f, 0.06f), new Vector3(2.1f, 0.22f, 0.24f), rot);
                    foreach (float s in new[] { -0.88f, 0.88f })
                        frames.Box(On(door.at + s, 1.15f, 0.04f), new Vector3(0.14f, 2.3f, 0.16f), rot);

                    if (solidDoors) paint.Box(On(door.at, 1.15f, 0.03f), new Vector3(1.55f, 2.28f, 0.08f), rot);
                    else
                    {
                        // An open door: the leaf swung back against the inside of the wall.
                        var hinge = centre + along * (door.at - 0.8f) - outward * 0.4f;
                        var leafRot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-outward.y, outward.x) * Mathf.Rad2Deg + 180f, 0f);
                        paint.Box(f.P(hinge.x - outward.x * 0.35f, 1.1f, hinge.y - outward.y * 0.35f), new Vector3(0.7f, 2.2f, 0.06f), leafRot);
                    }
                }

                // Windows between the doors, one every two and a half metres or so.
                bool open = openWindowsOn != null && System.Array.IndexOf(openWindowsOn, face) >= 0;
                if (open) continue;   // the real openings on this face are dressed below

                for (float s = -length * 0.5f + 1.4f; s < length * 0.5f - 1.2f; s += Rand(rng, 2.2f, 3.2f))
                {
                    bool clash = false;
                    foreach (var door in doorsHere) if (Mathf.Abs(s - door.at) < 1.6f) clash = true;
                    if (clash || rng.NextDouble() < 0.25) continue;
                    Window(rng, frames, dark, paint, grilles, On, rot, s, 1.6f);
                }

                // Sometimes an air-conditioner, high on a side wall.
                if (face >= 2 && rng.NextDouble() < 0.35)
                    grilles.Box(On(Rand(rng, -length * 0.25f, length * 0.25f), storey - 1.1f, 0.2f), new Vector3(0.85f, 0.55f, 0.4f), rot);
            }

            // Frames round the real window openings of an open house, on the faces that have them.
            if (openWindowsOn != null)
            {
                // The front (0) has one window, at the far side from its door; the -u face (2) two.
                var frontDoor = doors.Find(x => x.face == 0).at;
                float fw = frontDoor > 0f ? -hw * 0.6f : hw * 0.6f;
                var (c0, a0, o0, _) = Face(0);
                Vector3 On0(float s, float y, float out_) { var p = c0 + a0 * s + o0 * out_; return f.P(p.x, y, p.y); }
                OpenWindowFrame(frames, paint, On0, FaceRot(a0), fw, rng);

                var (c2, a2, o2, _) = Face(2);
                Vector3 On2(float s, float y, float out_) { var p = c2 + a2 * s + o2 * out_; return f.P(p.x, y, p.y); }
                OpenWindowFrame(frames, paint, On2, FaceRot(a2), -d * 0.25f, rng);
                OpenWindowFrame(frames, paint, On2, FaceRot(a2), d * 0.25f, rng);
            }

            Visual(parent, "Plinth", plinth, _plinthMat, layer);
            Visual(parent, "Beams", beams, _timberMat, layer);
            Visual(parent, "Frames", frames, _timberMat, layer);
            Visual(parent, "Windows", dark, _shadowMat, layer);
            Visual(parent, "Paint", paint, _doorPaints[rng.Next(_doorPaints.Length)], layer);
            Visual(parent, "Fittings", grilles, _acMat, layer);
        }

        private delegate Vector3 OnFace(float along, float y, float outward);

        /// <summary>A window on a solid wall: a dark recess, a timber frame, and shutters or a grille.</summary>
        private static void Window(System.Random rng, MeshBuild frames, MeshBuild dark, MeshBuild paint, MeshBuild grilles,
                                   OnFace on, Quaternion rot, float s, float sill)
        {
            const float ww = 0.8f, wh = 0.95f;
            float cy = sill + wh * 0.5f;
            dark.Box(on(s, cy, 0.02f), new Vector3(ww, wh, 0.06f), rot);
            frames.Box(on(s, sill - 0.05f, 0.06f), new Vector3(ww + 0.3f, 0.1f, 0.18f), rot);
            frames.Box(on(s, sill + wh + 0.06f, 0.06f), new Vector3(ww + 0.3f, 0.12f, 0.18f), rot);

            if (rng.NextDouble() < 0.55)
            {
                // Shutters, open against the wall either side.
                foreach (float side in new[] { -1f, 1f })
                    paint.Box(on(s + side * (ww * 0.5f + 0.22f), cy, 0.05f), new Vector3(0.42f, wh, 0.05f), rot);
            }
            else
            {
                for (float g = -ww * 0.5f + 0.13f; g < ww * 0.5f; g += 0.18f)
                    grilles.Box(on(s + g, cy, 0.06f), new Vector3(0.03f, wh, 0.03f), rot);
            }
        }

        /// <summary>A frame and, sometimes, shutters round a window that is a real hole in the wall.</summary>
        private static void OpenWindowFrame(MeshBuild frames, MeshBuild paint, OnFace on, Quaternion rot, float s, System.Random rng)
        {
            frames.Box(on(s, 1.05f, 0.08f), new Vector3(1.3f, 0.1f, 0.22f), rot);
            frames.Box(on(s, 2.16f, 0.08f), new Vector3(1.3f, 0.12f, 0.22f), rot);
            if (rng.NextDouble() < 0.6)
                foreach (float side in new[] { -1f, 1f })
                    paint.Box(on(s + side * 0.78f, 1.6f, 0.05f), new Vector3(0.5f, 1.0f, 0.05f), rot);
        }

        /// <summary>Windows on a second storey: small ones, high on each face.</summary>
        private static void UpperWindows(Transform parent, int layer, System.Random rng, Frame f, float cu, float cv, float w,
                                         float d, float storey)
        {
            var frames = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var paint = new MeshBuild { UVScale = 0.5f };
            var grilles = new MeshBuild { UVScale = 0.5f };
            float hw = w * 0.5f, hd = d * 0.5f;

            foreach (int face in new[] { 0, 2, 3 })
            {
                Vector2 centre = face == 0 ? new Vector2(cu, cv - hd) : face == 2 ? new Vector2(cu - hw, cv) : new Vector2(cu + hw, cv);
                Vector2 along = face == 0 ? Vector2.right : Vector2.up;
                Vector2 outward = face == 0 ? Vector2.down : face == 2 ? Vector2.left : Vector2.right;
                float length = face == 0 ? w : d;
                var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-along.y, along.x) * Mathf.Rad2Deg, 0f);
                Vector3 On(float s, float y, float o) { var p = centre + along * s + outward * o; return f.P(p.x, y, p.y); }

                for (float s = -length * 0.5f + 1.2f; s < length * 0.5f - 1f; s += 2.4f)
                    if (rng.NextDouble() < 0.7) Window(rng, frames, dark, paint, grilles, On, rot, s, storey + 1.1f);
            }

            Visual(parent, "Frames", frames, _timberMat, layer);
            Visual(parent, "Windows", dark, _shadowMat, layer);
            Visual(parent, "Paint", paint, _doorPaints[rng.Next(_doorPaints.Length)], layer);
            Visual(parent, "Fittings", grilles, _steelMat, layer);
        }

        /// <summary>
        /// What is on a flat roof: a water tank on a stand, a satellite dish, an aerial,
        /// washing on a line. On a roof a player can stand on they are solid, and cover.
        /// </summary>
        private static void RoofDressing(Transform parent, int layer, System.Random rng, Frame f, float cu, float cv, float w,
                                         float d, float roofY, bool walkable)
        {
            var tanks = new MeshBuild { UVScale = 0.5f };
            var metal = new MeshBuild { UVScale = 0.5f };
            var dish = new MeshBuild { UVScale = 0.5f };
            var cloth = new MeshBuild { UVScale = 0.5f };
            float hw = Mathf.Max(0.5f, w * 0.5f - 1f), hd = Mathf.Max(0.5f, d * 0.5f - 1f);

            if (rng.NextDouble() < 0.75)
            {
                var at = f.P(cu + Rand(rng, -hw, hw), roofY, cv + Rand(rng, -hd, hd));
                foreach (var o in new[] { new Vector3(-0.4f, 0f, -0.4f), new Vector3(0.4f, 0f, -0.4f), new Vector3(-0.4f, 0f, 0.4f), new Vector3(0.4f, 0f, 0.4f) })
                    metal.Box(at + o + Vector3.up * 0.3f, new Vector3(0.06f, 0.6f, 0.06f), f.Rot);
                tanks.Tube(at + Vector3.up * 0.6f, at + Vector3.up * 1.7f, 0.6f, 0.6f, 12);
                tanks.Tube(at + Vector3.up * 1.7f, at + Vector3.up * 1.85f, 0.6f, 0.2f, 12);
            }

            if (rng.NextDouble() < 0.45)
            {
                var at = f.P(cu + Rand(rng, -hw, hw), roofY, cv + Rand(rng, -hd, hd));
                metal.Tube(at, at + Vector3.up * 1.1f, 0.04f, 0.04f, 5);
                var face = Quaternion.Euler(0f, Rand(rng, 100f, 170f), 0f) * new Vector3(0f, 0.5f, 1f).normalized;
                dish.Tube(at + Vector3.up * 1.1f, at + Vector3.up * 1.1f + face * 0.18f, 0.45f, 0.5f, 12);
            }

            if (rng.NextDouble() < 0.3)
            {
                var at = f.P(cu + Rand(rng, -hw, hw), roofY, cv + Rand(rng, -hd, hd));
                metal.Tube(at, at + Vector3.up * 3.2f, 0.03f, 0.02f, 4);
                for (int i = 0; i < 4; i++)
                    metal.Box(at + Vector3.up * (2.2f + i * 0.28f), new Vector3(1.2f - i * 0.2f, 0.025f, 0.025f), f.Rot);
            }

            if (rng.NextDouble() < 0.3 && w > 4f)
            {
                // A washing line between two poles, and what is hanging on it.
                var a = f.P(cu - hw, roofY, cv + Rand(rng, -hd, hd));
                var b = f.P(cu + hw, roofY, cv + Rand(rng, -hd, hd));
                metal.Tube(a, a + Vector3.up * 1.8f, 0.03f, 0.03f, 4);
                metal.Tube(b, b + Vector3.up * 1.8f, 0.03f, 0.03f, 4);
                metal.Tube(a + Vector3.up * 1.75f, b + Vector3.up * 1.75f, 0.008f, 0.008f, 3);
                int items = 3 + rng.Next(3);
                for (int i = 0; i < items; i++)
                {
                    float t0 = (i + 0.2f) / items, t1 = t0 + Rand(rng, 0.08f, 0.14f);
                    var p0 = Vector3.Lerp(a, b, t0) + Vector3.up * 1.74f;
                    var p1 = Vector3.Lerp(a, b, t1) + Vector3.up * 1.74f;
                    float drop = Rand(rng, 0.5f, 0.9f);
                    AddUp(cloth, p0, p1, p1 + Vector3.down * drop, p0 + Vector3.down * drop);
                    AddDown(cloth, p0, p1, p1 + Vector3.down * drop, p0 + Vector3.down * drop);
                }
            }

            if (tanks.Triangles.Count > 0)
            {
                var go = MeshObject(parent, "RoofTank", ToMesh(tanks, DenseKey("rooftank")), rng.NextDouble() < 0.6 ? _tankBlackMat : _dishMat,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal", collider: walkable);
                NoStanding(go);
                Hide(go);
            }
            if (metal.Triangles.Count > 0) Visual(parent, "RoofMetal", metal, _steelMat, layer);
            if (dish.Triangles.Count > 0) Visual(parent, "Dish", dish, _dishMat, layer);
            if (cloth.Triangles.Count > 0) Visual(parent, "Washing", cloth, _clothPaints[rng.Next(_clothPaints.Length)], layer);
        }

        /// <summary>
        /// Furniture in one room of an open house, kept off the doorways: a bench or a bed
        /// against the back wall, a low table and floor cushions on a rug, shelves, jars.
        /// All of it solid and low -- cover, and nothing an agent could be put on top of.
        /// </summary>
        private static void Furnish(Transform parent, int layer, int backdrop, System.Random rng, Frame f,
                                    float cu, float v0, float uMax, float v1, float uMin, float doorA, float doorB)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var soft = new MeshBuild { UVScale = 0.5f };
            var rug = new MeshBuild { UVScale = 0.5f };

            float midV = (v0 + v1) * 0.5f, depth = v1 - v0;
            bool Clear(float u) => Mathf.Abs(u - doorA) > 1.4f && Mathf.Abs(u - doorB) > 1.4f;

            // A bench along the -u wall, where there is no door.
            if (depth > 2.6f)
            {
                wood.Box(f.P(uMin + 0.45f, 0.22f, midV), new Vector3(0.8f, 0.44f, Mathf.Min(2f, depth - 1.2f)), f.Rot);
                soft.Box(f.P(uMin + 0.45f, 0.5f, midV), new Vector3(0.7f, 0.12f, Mathf.Min(1.9f, depth - 1.3f)), f.Rot);
            }

            // A low table on a rug in the middle.
            float tu = (uMin + uMax) * 0.5f + 0.4f;
            AddUp(rug, f.P(tu - 1.2f, 0.035f, midV - 0.9f), f.P(tu - 1.2f, 0.035f, midV + 0.9f),
                  f.P(tu + 1.2f, 0.035f, midV + 0.9f), f.P(tu + 1.2f, 0.035f, midV - 0.9f));
            if (Clear(tu)) wood.Box(f.P(tu, 0.2f, midV), new Vector3(1.1f, 0.4f, 0.7f), f.Rot);

            // Shelves against the +u wall, jars on the floor in a corner.
            if (Clear(uMax - 0.3f)) wood.Box(f.P(uMax - 0.25f, 0.9f, midV), new Vector3(0.4f, 1.8f, 1.2f), f.Rot);
            for (int i = 0; i < 3; i++)
            {
                var at = f.P(uMin + 0.35f + i * 0.4f, 0f, v0 + 0.35f);
                if (!Clear(uMin + 0.35f + i * 0.4f)) continue;
                soft.Tube(at, at + Vector3.up * 0.55f, 0.18f, 0.12f, 8);
            }

            if (wood.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Furniture", ToMesh(wood, DenseKey("furniture")), _timberMat, Vector3.zero,
                                      Quaternion.identity, Vector3.one, layer, "Wood"));
            if (soft.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Furnishings", ToMesh(soft, DenseKey("furnish")), _clothPaints[rng.Next(_clothPaints.Length)],
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood"));
            Flat(parent, backdrop, "Rug", rug, DenseKey("rug"), _clothPaints[rng.Next(_clothPaints.Length)]);
        }

        /// <summary>
        /// A yard wall across the front of a lot, with a gate in the front and one in a side,
        /// and the gate leaves standing open against the inside.
        /// </summary>
        private static void YardWall(Transform parent, int layer, System.Random rng, Frame f, float lot, float houseFrontV, Material mat)
        {
            var wall = new MeshBuild { UVScale = 0.28f };
            const float h = 2.3f, t = 0.35f;
            float e = lot * 0.5f - 0.3f;

            float frontGate = Rand(rng, -e * 0.4f, e * 0.4f);
            float sideSign = rng.Next(2) == 0 ? -1f : 1f;
            float sideGate = (houseFrontV + (-e)) * 0.5f;

            WallRun(wall, f, new Vector2(-e, -e), new Vector2(e, -e), 0f, h, t,
                    new List<Opening> { new Opening(e + frontGate, 2.4f, 0f, h + 1f) });
            WallRun(wall, f, new Vector2(-e, -e + t * 0.5f), new Vector2(-e, houseFrontV), 0f, h, t,
                    sideSign < 0 ? new List<Opening> { new Opening(sideGate - (-e + t * 0.5f), 2.0f, 0f, h + 1f) } : null);
            WallRun(wall, f, new Vector2(e, -e + t * 0.5f), new Vector2(e, houseFrontV), 0f, h, t,
                    sideSign > 0 ? new List<Opening> { new Opening(sideGate - (-e + t * 0.5f), 2.0f, 0f, h + 1f) } : null);

            NoStanding(MeshObject(parent, "YardWall", ToMesh(wall, DenseKey("yardwall")), mat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            // The gate leaves, swung in, and a few things in the yard.
            var leaves = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -1f, 1f })
            {
                var hinge = f.P(frontGate + s * 1.2f, 1.0f, -e + 0.2f);
                leaves.Box(hinge + f.Axis(new Vector3(0f, 0f, 0.55f)), new Vector3(0.06f, 2.0f, 1.1f), f.Rot);
            }
            Visual(parent, "GateLeaves", leaves, _doorPaints[rng.Next(_doorPaints.Length)], layer);

            var things = new MeshBuild { UVScale = 0.5f };
            float yardDepth = houseFrontV - (-e);
            if (yardDepth > 2.5f)
            {
                Drum(things, f.P(-e + 0.7f, 0f, houseFrontV - 0.7f));
                Crate(things, f.P(e - 0.8f, 0f, houseFrontV - 0.8f), 0.7f, 10f);
                things.Tube(f.P(e - 1.6f, 0f, -e + 0.8f), f.P(e - 1.6f, 0.7f, -e + 0.8f), 0.3f, 0.22f, 8);
            }
            if (things.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "YardThings", ToMesh(things, DenseKey("yardthings")), _drumMat, Vector3.zero,
                                      Quaternion.identity, Vector3.one, layer, "Metal"));
        }

        /// <summary>
        /// Things against a house in the space its lot leaves round it: drums, crates, a
        /// stack of tyres, a gas bottle, a clay jar. Only where there is room outside the walls.
        /// </summary>
        private static void LotClutter(Transform parent, int layer, System.Random rng, Frame f, float lot, float cu, float cv, float w, float d)
        {
            var build = new MeshBuild { UVScale = 0.5f };
            float e = lot * 0.5f - 0.5f;
            foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                float u = su * e, v = sv * e;
                if (Mathf.Abs(u - cu) < w * 0.5f + 0.6f && Mathf.Abs(v - cv) < d * 0.5f + 0.6f) continue;
                if (rng.NextDouble() < 0.4) continue;

                var at = f.P(u, 0f, v);
                switch (rng.Next(4))
                {
                    case 0: Drum(build, at); break;
                    case 1: Crate(build, at, Rand(rng, 0.5f, 0.8f), Rand(rng, 0f, 90f)); break;
                    case 2:
                        for (int i = 0; i < 3; i++) build.Tube(at + Vector3.up * (i * 0.24f), at + Vector3.up * (i * 0.24f + 0.22f), 0.36f, 0.36f, 10);
                        break;
                    default: build.Tube(at, at + Vector3.up * 0.8f, 0.3f, 0.2f, 8); break;
                }
            }

            if (build.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "LotClutter", ToMesh(build, DenseKey("lotclutter")),
                                      rng.NextDouble() < 0.5 ? _drumMat : _timberMat, Vector3.zero, Quaternion.identity,
                                      Vector3.one, layer, "Metal"));
        }

        /// <summary>An empty lot: a low wall stub, rubble, a burnt-out car, dry scrub.</summary>
        private static void EmptyLot(Transform parent, int layer, int backdrop, System.Random rng, Frame f, float lot)
        {
            var build = new MeshBuild { UVScale = 0.3f };
            for (int i = 0; i < 6; i++)
                build.Box(f.P(Rand(rng, -lot * 0.35f, lot * 0.35f), 0.25f, Rand(rng, -lot * 0.35f, lot * 0.35f)),
                          new Vector3(Rand(rng, 0.5f, 1.4f), 0.5f, Rand(rng, 0.4f, 1f)), f.Rot * Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
            build.Box(f.P(-lot * 0.5f + 0.4f, 0.6f, 0f), new Vector3(0.4f, 1.2f, lot * 0.6f), f.Rot);
            Solid(parent, "LotRubble", build, _adobeTints[rng.Next(_adobeTints.Length)], layer);

            if (rng.NextDouble() < 0.6)
                BuildVehicle(parent, layer, rng, f.P2(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)), Rand(rng, 0f, 360f),
                             VehicleKind.Pickup, _burntMat);
        }

        /// <summary>
        /// The mosque: a prayer hall with a dome on a drum and a crescent-topped finial, a
        /// crenellated parapet, and an arched door facing the street.
        /// </summary>
        private static void BuildMosque(Transform parent, int layer, Frame f)
        {
            const float size = 10f, height = 5.4f;
            var hall = new MeshBuild { UVScale = 0.25f };
            hall.Box(f.P(0f, height * 0.5f, 0f), new Vector3(size, height, size), f.Rot);

            // Crenellations.
            for (float s = -size * 0.5f + 0.4f; s <= size * 0.5f - 0.4f; s += 0.9f)
                foreach (var (u, v) in new[] { (s, -size * 0.5f + 0.2f), (s, size * 0.5f - 0.2f), (-size * 0.5f + 0.2f, s), (size * 0.5f - 0.2f, s) })
                    hall.Box(f.P(u, height + 0.3f, v), new Vector3(0.45f, 0.6f, 0.45f), f.Rot);

            var go = Solid(parent, "Mosque", hall, _adobeTints[1], layer);
            if (go != null) Mark(go, new Color(0.72f, 0.64f, 0.48f), 4);
            SealLocal(parent, f, 0f, height * 0.5f, 0f, new Vector3(size, height, size));

            // The dome: a drum, then rings stepping in.
            var dome = new MeshBuild { UVScale = 0.3f };
            var top = f.P(0f, height, 0f);
            dome.Tube(top, top + Vector3.up * 1f, 3.7f, 3.7f, 20);
            const int rings = 7;
            const float R = 3.6f;
            for (int i = 0; i < rings; i++)
            {
                float a0 = i * Mathf.PI * 0.5f / rings, a1 = (i + 1) * Mathf.PI * 0.5f / rings;
                dome.Tube(top + Vector3.up * (1f + Mathf.Sin(a0) * R * 1.15f), top + Vector3.up * (1f + Mathf.Sin(a1) * R * 1.15f),
                          Mathf.Max(0.05f, Mathf.Cos(a0) * R), Mathf.Max(0.05f, Mathf.Cos(a1) * R), 20);
            }
            var domeGo = MeshObject(parent, "Dome", ToMesh(dome, DenseKey("dome")), _doorPaints[0], Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Concrete");
            NoStanding(domeGo);
            Hide(domeGo);

            var finial = new MeshBuild { UVScale = 0.5f };
            var tip = top + Vector3.up * (1f + R * 1.15f);
            finial.Tube(tip, tip + Vector3.up * 1.4f, 0.06f, 0.04f, 6);
            finial.Tube(tip + Vector3.up * 0.5f, tip + Vector3.up * 0.75f, 0.2f, 0.2f, 8);
            Visual(parent, "Finial", finial, _clothSaffron, layer);

            // An arched door on the -v face: a tall dark recess under a pointed head.
            var dark = new MeshBuild { UVScale = 0.5f };
            var door = f.P(0f, 0f, -size * 0.5f - 0.04f);
            dark.Box(door + Vector3.up * 1.6f, new Vector3(2f, 3.2f, 0.08f), f.Rot);
            for (int i = 0; i < 6; i++)
            {
                float y = 3.2f + i * 0.15f;
                dark.Box(door + Vector3.up * y, new Vector3(2f - i * 0.34f, 0.15f, 0.08f), f.Rot);
            }
            for (float s = -3.5f; s <= 3.5f; s += 1.75f)
                if (Mathf.Abs(s) > 1.5f)
                    dark.Box(f.P(s, 3.2f, -size * 0.5f - 0.04f), new Vector3(0.6f, 1.4f, 0.08f), f.Rot);
            Visual(parent, "MosqueOpenings", dark, _shadowMat, layer);
        }

        // ==================================================================
        // Poles and wires
        // ==================================================================
        /// <summary>
        /// A run of wooden poles with a crossarm, and three wires sagging between each pair.
        /// Poles are solid and off the bake; the wires are drawn only.
        /// </summary>
        private static void BuildPoleRun(Transform parent, int layer, int backdrop, List<Vector3> poles, float height)
        {
            if (poles.Count == 0) return;

            var wood = new MeshBuild { UVScale = 0.5f };
            var wires = new MeshBuild { UVScale = 0.5f };
            var arms = new Vector3[poles.Count];

            for (int i = 0; i < poles.Count; i++)
            {
                var p = poles[i];
                var next = poles[Mathf.Min(i + 1, poles.Count - 1)];
                var prev = poles[Mathf.Max(i - 1, 0)];
                var run = next - prev;
                run.y = 0f;
                if (run.sqrMagnitude < 0.01f) run = Vector3.forward;
                arms[i] = Vector3.Cross(Vector3.up, run.normalized);

                wood.Tube(p + Vector3.down * 0.5f, p + Vector3.up * height, 0.14f, 0.1f, 7);
                wood.Box(p + Vector3.up * (height - 0.5f), new Vector3(0.12f, 0.12f, 2.2f), Quaternion.LookRotation(arms[i]));
            }

            for (int i = 0; i + 1 < poles.Count; i++)
            {
                var a = poles[i];
                var b = poles[i + 1];
                if (Vector3.Distance(a, b) > 70f) continue;

                foreach (float s in new[] { -0.95f, 0f, 0.95f })
                {
                    var from = a + Vector3.up * (height - 0.35f) + arms[i] * s;
                    var to = b + Vector3.up * (height - 0.35f) + arms[i + 1] * s;
                    float sag = Vector3.Distance(from, to) * 0.035f;
                    const int segments = 8;
                    for (int k = 0; k < segments; k++)
                    {
                        float t0 = k / (float)segments, t1 = (k + 1) / (float)segments;
                        var p0 = Vector3.Lerp(from, to, t0) + Vector3.down * sag * 4f * t0 * (1f - t0);
                        var p1 = Vector3.Lerp(from, to, t1) + Vector3.down * sag * 4f * t1 * (1f - t1);
                        wires.Tube(p0, p1, 0.02f, 0.02f, 3);
                    }
                }
            }

            var poleGo = MeshObject(parent, "Poles", ToMesh(wood, DenseKey("poles")), _timberMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Wood");
            NoStanding(poleGo);
            Hide(poleGo);
            if (wires.Triangles.Count > 0)
            {
                var wireGo = MeshObject(parent, "Wires", ToMesh(wires, DenseKey("wires")), _tankBlackMat, Vector3.zero,
                                        Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                Hide(wireGo);
                wireGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
#endif
