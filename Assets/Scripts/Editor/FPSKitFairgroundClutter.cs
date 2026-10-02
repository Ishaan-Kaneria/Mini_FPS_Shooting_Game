#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What a place that stopped leaves lying about, and the lights that are still on.
    ///
    /// <b>Clutter has a reason to be where it is.</b> Overturned food carts and barricades go by
    /// the walkways, where people were; crates, pallets and drums in the service lanes, where
    /// the staff were; fallen sign boards and tyre stacks on the open ground between districts.
    /// Each piece is a handful of parts, never one block, and each is cover: the fairground is
    /// a place to fight in, and an arena with nothing to stand behind between its landmarks
    /// is a shooting range.
    ///
    /// <b>It runs late and asks the scene.</b> Everything before it has been built, so a
    /// piece is placed only if the keep-out circles, the walkways and the physics scene agree
    /// that nothing is there. It is batched by ninety-metre cell, so the physics scene holds a few
    /// dozen meshes and not a few thousand objects.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private sealed class ClutterCell
        {
            public readonly MeshBuild Wood = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Steel = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Paint = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Rubble = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Canvas = new MeshBuild { UVScale = 0.4f };
            public readonly MeshBuild Weeds = new MeshBuild { UVScale = 0.5f };
        }

        private static void BuildClutter(Transform root, int layer, int backdrop, float half)
        {
            var group = new GameObject("Clutter").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 1237 + 19);
            var cells = new Dictionary<Vector2Int, ClutterCell>();

            ClutterCell CellAt(Vector2 p)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / 90f), Mathf.FloorToInt(p.y / 90f));
                if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new ClutterCell();
                return cell;
            }

            float limit = half - BermToe - 12f;
            int placed = 0, tried = 0;
            var counts = new int[14];

            // Pieces are placed against something: a walkway's edge, a building or a ride, or -- for the
            // leisure pieces -- a quiet stretch a few metres back from a walkway. Nothing stands alone in
            // an open field, because nothing would have been put there.
            var lengths = new List<float>();
            float totalLength = 0f;
            foreach (var route in _fgRoutes)
            {
                float l = 0f;
                for (int i = 0; i < route.Points.Count - 1; i++) l += Vector2.Distance(route.Points[i], route.Points[i + 1]);
                lengths.Add(l);
                totalLength += l;
            }

            var bigKeeps = new List<Vector3>();
            foreach (var k in _fgKeep) if (k.z >= 6f) bigKeeps.Add(k);

            Vector2 OnRoute(float offsetMin, float offsetMax, out float edgeDistance)
            {
                float pick = (float)rng.NextDouble() * totalLength;
                int r = 0;
                while (r < lengths.Count - 1 && pick > lengths[r]) { pick -= lengths[r]; r++; }

                var pts = _fgRoutes[r].Points;
                int seg = 0;
                while (seg < pts.Count - 2 && pick > Vector2.Distance(pts[seg], pts[seg + 1])) { pick -= Vector2.Distance(pts[seg], pts[seg + 1]); seg++; }

                var a0 = pts[seg]; var b0 = pts[seg + 1];
                var dir = (b0 - a0).normalized;
                var across = new Vector2(-dir.y, dir.x) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                float off = Rand(rng, offsetMin, offsetMax);
                edgeDistance = off;
                return a0 + dir * Mathf.Min(pick, Vector2.Distance(a0, b0)) + across * (_fgRoutes[r].Width * 0.5f + off);
            }

            for (int attempt = 0; attempt < 3600 && placed < 520; attempt++)
            {
                tried++;
                int kind;
                Vector2 p;
                double mode = rng.NextDouble();
                float edge;

                if (mode < 0.58)
                {
                    // Beside a walkway: what people dropped, leaned and left.
                    p = OnRoute(1.2f, 4.2f, out edge);
                    double roll = rng.NextDouble();
                    kind = roll < 0.30 ? 0 : roll < 0.52 ? 2 : roll < 0.78 ? 4 : roll < 0.9 ? 5 : 3;
                }
                else if (mode < 0.82 && bigKeeps.Count > 0)
                {
                    // Against a building or a ride: the working clutter of whoever ran it.
                    var k = bigKeeps[rng.Next(bigKeeps.Count)];
                    float a = Rand(rng, 0f, 6.283f);
                    p = new Vector2(k.x, k.y) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (k.z + Rand(rng, 1.2f, 3.2f));
                    edge = RouteClearance(p);
                    double roll = rng.NextDouble();
                    kind = roll < 0.3 ? 1 : roll < 0.52 ? 3 : roll < 0.7 ? 9 : roll < 0.82 ? 7 : roll < 0.92 ? 8 : 6;
                }
                else
                {
                    // A quiet stretch a few metres back from a walkway: somewhere to sit.
                    p = OnRoute(6f, 13f, out edge);
                    kind = rng.NextDouble() < 0.35 ? 10 : rng.NextDouble() < 0.4 ? 11 : rng.NextDouble() < 0.5 ? 12 : 13;
                }

                if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.y) > limit) continue;
                edge = RouteClearance(p);

                float radius = kind == 0 ? 1.6f : kind == 6 ? 2.2f : kind == 10 ? 3.2f : kind == 11 ? 4.4f : kind == 12 ? 2.0f : kind == 13 ? 2.3f : 1.4f;
                if (!KeepClear(p, radius) || !OffSinks(p, radius + 1f)) continue;
                if (edge < 1.4f) continue;                                    // never on a walkway
                if (SlopeAt(p) > 0.35f) continue;
                if (!SpotEmpty(p, radius, 0.25f, 2.6f)) continue;

                var cell = CellAt(p);
                var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                float yaw = Rand(rng, 0f, 360f);

                switch (kind)
                {
                    case 0: FoodCart(cell, foot, yaw, rng); break;
                    case 1: CrateStack(cell, foot, yaw, rng); break;
                    case 2: Barricade(cell, foot, yaw, rng); break;
                    case 3: DrumGroup(cell, foot, yaw, rng); break;
                    case 4: SignPost(cell, foot, yaw, rng); break;
                    case 5: FallenLamp(cell, foot, yaw, rng); break;
                    case 6: CollapsedTent(cell, foot, yaw, rng); break;
                    case 7: TyreStack(cell, foot, yaw, rng); break;
                    case 8: RubblePile(cell, foot, yaw, rng); break;
                    case 10: Pergola(cell, foot, yaw, rng); break;
                    case 11: PicnicShelter(cell, foot, yaw, rng); break;
                    case 12: Statue(cell, foot, yaw, rng); break;
                    case 13: FlowerBed(cell, foot, yaw, rng); break;
                    default: PalletStack(cell, foot, yaw, rng); break;
                }

                // Weeds come with all of it.
                Tuft(cell.Weeds, foot, Rand(rng, 0.6f, 1.2f), rng.Next(1, 99999));
                Tuft(cell.Weeds, foot + new Vector3(Rand(rng, -1f, 1f), 0f, Rand(rng, -1f, 1f)), Rand(rng, 0.5f, 1.0f), rng.Next(1, 99999));
                Keep(p.x, p.y, radius * 0.6f);
                counts[kind]++;
                placed++;
            }

            // Grass pushing up through the open ground, in tufts, away from anything solid.
            int tufts = 0;
            for (int i = 0; i < 1500 && tufts < 900; i++)
            {
                var p = new Vector2(Rand(rng, -limit, limit), Rand(rng, -limit, limit));
                if (!KeepClear(p, 0.3f) || !OffSinks(p, 0.5f) || RouteClearance(p) < 0.2f) continue;

                var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                var cell = CellAt(p);
                Tuft(cell.Weeds, foot, Rand(rng, 0.7f, 1.5f), rng.Next(1, 99999));
                if (rng.NextDouble() < 0.3) Fern(cell.Weeds, foot + new Vector3(0.4f, 0f, 0.3f), Rand(rng, 0.9f, 1.6f), rng.Next(1, 99999));
                tufts++;
            }

            foreach (var cell in cells.Values)
            {
                RideSolid(group, layer, "ClutterWood", cell.Wood, _parkTimber, "Wood");
                RideSolid(group, layer, "ClutterSteel", cell.Steel, _parkSteel);
                RideSolid(group, layer, "ClutterPaint", cell.Paint, _parkPaint, "Wood");
                RideSolid(group, layer, "ClutterRubble", cell.Rubble, _parkRubble, "Concrete");
                RideDecor(group, "ClutterCanvas", cell.Canvas, _rideCanvasCream);
                EmitFoliage(group, backdrop, "ClutterWeeds", cell.Weeds, _fgFern);
            }

            Debug.Log($"[FPSKit] fairground clutter: {placed} piece(s) in {cells.Count} cell(s) ({tried} tried), {tufts} tuft(s). " +
                      $"carts {counts[0]}, crates {counts[1]}, barricades {counts[2]}, drums {counts[3]}, signs {counts[4]}, lamps {counts[5]}, " +
                      $"tents {counts[6]}, tyres {counts[7]}, rubble {counts[8]}, pallets {counts[9]}, pergolas {counts[10]}, shelters {counts[11]}, statues {counts[12]}, beds {counts[13]}.");
        }

        // ------------------------------------------------------------------
        // The pieces
        // ------------------------------------------------------------------
        /// <summary>A food cart on its side: body, serving hatch, two wheels, and the umbrella that went with it.</summary>
        private static void FoodCart(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, rng.NextDouble() < 0.6 ? Rand(rng, 70f, 95f) : Rand(rng, -6f, 6f));
            Vector3 P(float x, float y, float z) => foot + Quaternion.Euler(0f, yaw, 0f) * (new Vector3(0f, 0f, 0f)) + rot * new Vector3(x, y, z);

            c.Paint.Box(P(0f, 0.9f, 0f), new Vector3(1.8f, 1.1f, 1.1f), rot);
            c.Wood.Box(P(0f, 1.55f, 0f), new Vector3(1.9f, 0.1f, 1.2f), rot);
            c.Steel.Box(P(0f, 0.9f, 0.58f), new Vector3(1.2f, 0.6f, 0.05f), rot);
            for (int s = -1; s <= 1; s += 2)
                c.Steel.Tube(P(s * 0.95f, 0.35f, 0.4f), P(s * 1.05f, 0.35f, 0.4f), 0.35f, 0.35f, 10);
            c.Steel.Tube(P(0f, 1.6f, 0f), P(0.4f, 3.0f, 0.2f), 0.03f, 0.025f, 3);
            c.Canvas.Tri(P(0.4f, 3.0f, 0.2f), P(-1.2f, 2.2f, 1.0f), P(1.4f, 2.1f, 1.1f));
            c.Canvas.Tri(P(1.4f, 2.1f, 1.1f), P(-1.2f, 2.2f, 1.0f), P(0.4f, 3.0f, 0.2f));
        }

        private static void CrateStack(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float size = Rand(rng, 0.7f, 1.1f);
                var off = rot * new Vector3((i % 2) * 1.05f - 0.5f, 0f, (i / 2) * 1.05f - 0.3f);
                c.Wood.Box(foot + off + Vector3.up * (size * 0.5f), new Vector3(size, size, size), rot * Quaternion.Euler(0f, Rand(rng, -15f, 15f), 0f));
                if (rng.NextDouble() < 0.35)
                    c.Wood.Box(foot + off + Vector3.up * (size + 0.3f), new Vector3(size * 0.8f, 0.6f, size * 0.8f), rot * Quaternion.Euler(Rand(rng, -9f, 9f), Rand(rng, 0f, 90f), Rand(rng, -9f, 9f)));
            }
        }

        /// <summary>A striped trestle barrier: two A-frames and the planks between.</summary>
        private static void Barricade(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            for (int s = -1; s <= 1; s += 2)
            {
                c.Steel.Tube(P(s * 1.3f, 0f, -0.35f), P(s * 1.3f, 1.1f, 0f), 0.04f, 0.04f, 3);
                c.Steel.Tube(P(s * 1.3f, 0f, 0.35f), P(s * 1.3f, 1.1f, 0f), 0.04f, 0.04f, 3);
            }

            bool knocked = rng.NextDouble() < 0.3;
            c.Paint.Box(P(0f, knocked ? 0.35f : 0.95f, 0f), new Vector3(3f, 0.22f, 0.05f), rot * Quaternion.Euler(0f, 0f, knocked ? 12f : 0f));
            c.Paint.Box(P(0f, knocked ? 0.2f : 0.65f, 0f), new Vector3(3f, 0.22f, 0.05f), rot * Quaternion.Euler(0f, 0f, knocked ? -9f : 0f));
        }

        private static void DrumGroup(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var at = foot + new Vector3(Rand(rng, -0.9f, 0.9f), 0f, Rand(rng, -0.9f, 0.9f));
                if (rng.NextDouble() < 0.28)
                {
                    var dir = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f) * Vector3.forward;
                    c.Steel.Tube(at + Vector3.up * 0.3f - dir * 0.45f, at + Vector3.up * 0.3f + dir * 0.45f, 0.3f, 0.3f, 8);
                }
                else
                {
                    c.Steel.Tube(at, at + Vector3.up * 0.9f, 0.3f, 0.3f, 8);
                    c.Steel.Tube(at + Vector3.up * 0.9f, at + Vector3.up * 0.95f, 0.31f, 0.31f, 8);
                }
            }
        }

        /// <summary>A sign on a post, leaning, with an arrow for a head and the lettering gone.</summary>
        private static void SignPost(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(Rand(rng, -6f, 6f), yaw, Rand(rng, -10f, 10f));
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            c.Steel.Tube(P(0f, 0f, 0f), P(0f, 3.2f, 0f), 0.06f, 0.05f, 4);
            c.Paint.Box(P(0.55f, 2.7f, 0f), new Vector3(1.4f, 0.55f, 0.06f), rot);
            c.Paint.Tri(P(1.25f, 2.98f, 0.03f), P(1.25f, 2.42f, 0.03f), P(1.85f, 2.7f, 0.03f));
            c.Paint.Tri(P(1.25f, 2.42f, -0.03f), P(1.25f, 2.98f, -0.03f), P(1.85f, 2.7f, -0.03f));
            c.Paint.Box(P(-0.4f, 2.0f, 0f), new Vector3(0.9f, 0.4f, 0.05f), rot * Quaternion.Euler(0f, 0f, 14f));
        }

        private static void FallenLamp(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var a = foot + Vector3.up * 0.25f;
            var b = a + dir * 5.2f + Vector3.up * 0.2f;
            c.Steel.Tube(a, b, 0.12f, 0.085f, 5);
            c.Steel.Box(b + dir * 0.2f + Vector3.up * 0.2f, new Vector3(0.5f, 0.55f, 0.5f), Quaternion.LookRotation(dir));
            c.Steel.Tube(a - dir * 0.1f, a + Vector3.up * 0.7f, 0.34f, 0.2f, 6);
        }

        /// <summary>A tent that came down: poles lying across each other and the canvas over them.</summary>
        private static void CollapsedTent(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            c.Steel.Tube(P(-2f, 0.2f, -1.5f), P(2.2f, 0.9f, 1.4f), 0.07f, 0.06f, 4);
            c.Steel.Tube(P(2f, 0.2f, -1.5f), P(-2.2f, 1.1f, 1.4f), 0.07f, 0.06f, 4);
            c.Steel.Tube(P(0f, 0.2f, -2f), P(0f, 1.6f, 0.2f), 0.06f, 0.05f, 4);
            Cloth(c.Canvas, P(-3f, 0.7f, -2.6f), rot * new Vector3(6f, 0f, 0f), rot * new Vector3(0f, -0.2f, 5.2f), 5, 4, 0.55f, rng.Next(1, 99999), 0.1f);
            c.Wood.Box(P(1.5f, 0.15f, -2f), new Vector3(0.9f, 0.3f, 0.9f), rot);
        }

        private static void TyreStack(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            int n = 2 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var at = foot + new Vector3((i % 2) * 0.1f, i * 0.26f, 0f);
                c.Steel.Tube(at, at + Vector3.up * 0.24f, 0.42f, 0.42f, 10);
            }

            if (rng.NextDouble() < 0.6)
                c.Steel.Tube(foot + new Vector3(1.0f, 0.22f, 0.4f), foot + new Vector3(1.0f, 0.46f, 0.4f), 0.42f, 0.4f, 10);
        }

        private static void RubblePile(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            int n = 5 + rng.Next(6);
            for (int i = 0; i < n; i++)
            {
                float w = Rand(rng, 0.3f, 1.0f);
                var at = foot + new Vector3(Rand(rng, -1.2f, 1.2f), w * 0.3f + (i > 5 ? 0.4f : 0f), Rand(rng, -1.2f, 1.2f));
                c.Rubble.Box(at, new Vector3(w, w * Rand(rng, 0.5f, 1f), w * Rand(rng, 0.6f, 1.2f)), Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f), Rand(rng, -20f, 20f)));
            }

            if (rng.NextDouble() < 0.5)
                c.Steel.Tube(foot + new Vector3(-0.5f, 0.6f, 0f), foot + new Vector3(1.4f, 1.1f, 0.6f), 0.025f, 0.025f, 3);
        }

        private static void PalletStack(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int n = 1 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var at = foot + rot * new Vector3((i % 2) * 0.1f, 0.07f + i * 0.16f, 0f);
                for (int s = -1; s <= 1; s++)
                {
                    c.Wood.Box(at + rot * new Vector3(0f, 0f, s * 0.45f), new Vector3(1.2f, 0.1f, 0.12f), rot);
                    c.Wood.Box(at + rot * new Vector3(s * 0.55f, 0.1f, 0f), new Vector3(0.1f, 0.06f, 1.2f), rot);
                }
            }

            if (rng.NextDouble() < 0.5)
                c.Rubble.Box(foot + Vector3.up * (n * 0.16f + 0.15f), new Vector3(1.0f, 0.3f, 1.0f), rot);
        }

        /// <summary>A pergola: four posts, cross-beams, slats on top, a bench under it and a vine over.</summary>
        private static void Pergola(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    c.Wood.Tube(P(sx * 1.5f, 0f, sz * 1.5f), P(sx * 1.5f, 2.8f, sz * 1.5f), 0.12f, 0.1f, 5);

            for (int sz = -1; sz <= 1; sz += 2)
                c.Wood.Box(P(0f, 2.85f, sz * 1.5f), new Vector3(3.5f, 0.14f, 0.2f), rot);
            for (int i = 0; i < 8; i++)
            {
                if (rng.NextDouble() < 0.25) continue;
                c.Wood.Box(P(-1.45f + i * 0.41f, 3.0f, 0f), new Vector3(0.1f, 0.1f, 3.4f), rot);
            }

            Bench(c.Wood, P(0f, 0f, 0.9f), yaw + 180f, rng.NextDouble() < 0.25, rng);
            Vine(c.Weeds, P(1.4f, 2.9f, 1.4f), Rand(rng, 2f, 3.4f), rng.Next(1, 99999));
            Vine(c.Weeds, P(-1.4f, 2.9f, -1.4f), Rand(rng, 1.6f, 3f), rng.Next(1, 99999));
        }

        /// <summary>A picnic shelter: a roof on four posts over two tables with benches.</summary>
        private static void PicnicShelter(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    c.Steel.Tube(P(sx * 2.8f, 0f, sz * 2.2f), P(sx * 2.8f, 3.0f, sz * 2.2f), 0.1f, 0.09f, 5);

            // The roof: two slopes of sheet, one fallen in.
            c.Steel.Box(P(0f, 3.4f, 0f), new Vector3(0.1f, 0.1f, 4.8f), rot);
            if (rng.NextDouble() < 0.7)
                c.Canvas.Quad(P(-3.2f, 3.0f, -2.4f), P(0f, 3.5f, -2.4f), P(0f, 3.5f, 2.4f), P(-3.2f, 3.0f, 2.4f));
            if (rng.NextDouble() < 0.45)
                c.Canvas.Quad(P(0f, 3.5f, -2.4f), P(3.2f, 3.0f, -2.4f), P(3.2f, 3.0f, 2.4f), P(0f, 3.5f, 2.4f));
            else
                c.Canvas.Quad(P(0f, 3.5f, -2.4f), P(3.2f, 0.9f, -1f), P(3.2f, 0.9f, 2.6f), P(0f, 3.5f, 2.4f));

            for (int t = -1; t <= 1; t += 2)
            {
                c.Wood.Box(P(t * 1.5f, 0.78f, 0f), new Vector3(0.9f, 0.08f, 2.2f), rot);
                c.Wood.Box(P(t * 1.5f, 0.38f, -1.0f), new Vector3(0.9f, 0.08f, 0.1f), rot);
                c.Wood.Box(P(t * 1.5f, 0.38f, 1.0f), new Vector3(0.9f, 0.08f, 0.1f), rot);
                c.Wood.Box(P(t * 1.5f - 0.7f, 0.45f, 0f), new Vector3(0.3f, 0.06f, 2.0f), rot);
                c.Wood.Box(P(t * 1.5f + 0.7f, 0.45f, 0f), new Vector3(0.3f, 0.06f, 2.0f), rot);
            }
        }

        /// <summary>A statue of the park's founder, on a plinth: bust and hat, or lying in the grass.</summary>
        private static void Statue(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            c.Rubble.Box(P(0f, 0.65f, 0f), new Vector3(1.4f, 1.3f, 1.4f), rot);
            c.Rubble.Box(P(0f, 1.38f, 0f), new Vector3(1.7f, 0.16f, 1.7f), rot);

            bool fallen = rng.NextDouble() < 0.4;
            if (!fallen)
            {
                c.Rubble.Tube(P(0f, 1.46f, 0f), P(0f, 3.0f, 0f), 0.5f, 0.38f, 8);
                c.Rubble.Tube(P(0f, 3.0f, 0f), P(0f, 3.5f, 0f), 0.3f, 0.26f, 8);
                c.Rubble.Tube(P(0.4f, 2.7f, 0f), P(1.1f, 3.4f, 0.2f), 0.12f, 0.09f, 5);
                c.Rubble.Tube(P(0f, 3.5f, 0f), P(0f, 4.4f, 0f), 0.34f, 0.04f, 7);
            }
            else
            {
                c.Rubble.Tube(P(1.2f, 0.4f, 1.0f), P(3.0f, 0.5f, 1.6f), 0.5f, 0.38f, 8);
                c.Rubble.Tube(P(3.0f, 0.5f, 1.6f), P(3.5f, 0.55f, 1.7f), 0.3f, 0.26f, 8);
            }
        }

        /// <summary>A bed of flowers gone to seed: a border of stones and a bush of ragged leaf and weeds.</summary>
        private static void FlowerBed(ClutterCell c, Vector3 foot, float yaw, System.Random rng)
        {
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                var p = foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f;
                c.Rubble.Box(p + Vector3.up * 0.18f, new Vector3(0.6f, 0.36f, 0.4f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
            }

            LeafBlob(c.Weeds, foot + Vector3.up * 0.6f, Rand(rng, 0.7f, 1.0f), rng.Next(1, 99999));
            for (int i = 0; i < 6; i++)
                Tuft(c.Weeds, foot + new Vector3(Rand(rng, -1.1f, 1.1f), 0f, Rand(rng, -1.1f, 1.1f)), Rand(rng, 0.7f, 1.4f), rng.Next(1, 99999));
        }

        // ==================================================================
        // Light
        // ==================================================================
        /// <summary>
        /// What is still lit.
        ///
        /// <b>A park at dusk is a lighting design, not an absence of one.</b> The sun is low and does
        /// the base; each district then gets one warm light of its own, so a landmark across the park
        /// is a landmark you can see; and every hollow with a shaft is lit from inside, cold and weak,
        /// which is what makes a hole read as a hole from fifty metres.
        /// </summary>
        private static void BuildParkLights(Transform root, System.Random rng, float half)
        {
            var group = new GameObject("ParkLights").transform;
            group.SetParent(root, false);

            void Warm(string name, Vector2 at, float height, float range, float intensity, Color? colour = null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(at.x, GroundHeightAt(at.x, at.y) + height, at.y);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = colour ?? new Color(1f, 0.72f, 0.40f);
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }

            foreach (var plan in _ridePlans)
                Warm($"RideLight{plan.Kind}", plan.Centre, 9f, plan.Radius * 2.2f, 5.5f + (float)rng.NextDouble() * 2f);

            foreach (var sink in _sinks)
            {
                if (!sink.HasShaft) continue;
                Warm("ShaftGlow", sink.Centre, 1.4f, sink.Radius * 1.5f, 2.6f, new Color(0.42f, 0.66f, 0.8f));
            }

            Warm("GateLight", _entrancePlan.Centre, 5.5f, 34f, 5f);
            Warm("PlazaLight", new Vector2(0f, 14f), 6f, 34f, 4.4f);
            for (float z = -100f; z <= -40f; z += 30f)
                Warm($"MidwayLight{z}", new Vector2(0f, z), 5.2f, 30f, 4.6f);

            Warm("HallForecourtLight", new Vector2(_hallPlan.Centre.x, 66f), 5.5f, 28f, 4.2f);
            Warm("PondLight", _pondPlan.Centre, 4.5f, 24f, 3.2f, new Color(0.75f, 0.85f, 1f));
            Warm("BumperLight", _bumperPlan.Centre, 6f, 28f, 4f);
            Warm("MazeEntranceLight", new Vector2(-60f, 84f), 4.2f, 18f, 3f);
            Warm("YardLight", _maintenancePlan.Centre, 6.5f, 28f, 3.6f, new Color(0.83f, 0.67f, 0.42f));
            Warm("ClockLight2", _clockPlan.Centre, 3.2f, 14f, 2.6f);
        }
    }
}
#endif
