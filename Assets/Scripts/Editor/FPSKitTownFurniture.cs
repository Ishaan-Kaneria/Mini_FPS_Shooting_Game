#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The pieces that make a house's rooms look lived in (2026-10-22, after the first interiors read as big empty
    /// boxes): a dining room's table for six and its sideboard, wardrobes, bedside tables with lamps, armchairs, floor
    /// vases, curtains at the windows, a white ceiling.
    ///
    /// Box furniture (tables, sideboards, wardrobes, armchairs) is built into the chunk mesh like the beds and kitchens:
    /// it is physical and costs no object. Poly Haven props are kept to what a box cannot be (chairs, pots, vases,
    /// plants), because every prop is an object with a collider and the town has to fit in the editor's memory
    /// (Docs/DESIGN_NOTES.md "The town must fit in the editor's memory"). Curtains, seams and handles go in the detail mesh.
    /// How much a room gets grows with its floor area, so a 30 m2 room is not furnished like a 12 m2 one.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _ceilingWhite;
        private static Material[] _curtains;

        private static void ResolveFurnitureMaterials()
        {
            if (_ceilingWhite != null) return;
            // A painted ceiling is one flat white. Facing down, it is lit only by the sand-coloured light coming up off the
            // ground: textured plaster read tan and blotchy (0.95x), then brown and blotchier (1.58x). A little emission stands
            // in for the room's lamp and the light its walls throw up, and it is kept out of the probe bake.
            _ceilingWhite = TownFlat("CeilingPaint", new Color(0.88f, 0.87f, 0.84f), 0.06f);
            _ceilingWhite.EnableKeyword("_EMISSION");
            _ceilingWhite.SetColor("_EmissionColor", new Color(0.17f, 0.17f, 0.16f));
            _ceilingWhite.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(_ceilingWhite);
            _curtains = new[]
            {
                TownFlat("CurtainLinen", new Color(0.84f, 0.80f, 0.71f), 0.12f), TownFlat("CurtainSand", new Color(0.70f, 0.58f, 0.42f), 0.12f),
                TownFlat("CurtainOlive", new Color(0.40f, 0.42f, 0.27f), 0.12f), TownFlat("CurtainWine", new Color(0.42f, 0.13f, 0.14f), 0.12f),
                TownFlat("CurtainTeal", new Color(0.16f, 0.33f, 0.35f), 0.12f), TownFlat("CurtainWhite", new Color(0.90f, 0.89f, 0.86f), 0.12f),
            };
        }

        /// <summary>Extra pieces for a room by its kind and size (after its main furniture has been placed).</summary>
        private static void FurnishMore(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, bool old, System.Random own)
        {
            float area = r.r.width * r.r.height;
            int extra = Mathf.Clamp(Mathf.FloorToInt(area / 11f), 0, 3);
            switch (r.kind)
            {
                case "living":
                    for (int i = 0; i < Mathf.Max(1, extra); i++) Armchair(m, f, r, own);
                    if (extra >= 2) Sideboard(m, det, props, f, r, own);
                    FloorVase(props, f, r, own);
                    if (extra >= 3) FloorVase(props, f, r, own);
                    break;
                case "bedroom":
                    if (area > 13f) Wardrobe(m, det, f, r, own);
                    if (extra >= 2) Sideboard(m, det, props, f, r, own);           // a chest of drawers
                    if (own.NextDouble() < 0.5 || extra >= 2) FloorVase(props, f, r, own);
                    break;
                case "majlis":
                    if (own.NextDouble() < 0.6) PropAgainstWall(props, f, r, own, "Lantern_01", 0.35f, 0.35f, false, height: 0.5f);
                    if (extra >= 2) FloorVase(props, f, r, own);
                    break;
                case "kitchen":
                    if (extra >= 2 && own.NextDouble() < 0.6) PropAgainstWall(props, f, r, own, "painted_wooden_shelves", 0.55f, 0.4f, false);
                    break;
            }
        }

        /// <summary>A dining room: a table for six with a bowl of fruit, a sideboard with things on it, a glass-fronted
        /// cabinet, a floor vase.</summary>
        private static void Dining(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            bool alongA = r.r.width >= r.r.height;
            Vector2 ax = alongA ? Vector2.right : Vector2.up, bx = alongA ? Vector2.up : Vector2.right;
            // A table for six where it fits, else for four: the middle of the room first, then anywhere on a 40 cm grid
            // (the lane to a doorway beyond often runs through the middle; 20 of 52 dining rooms were left bare).
            float L = 0f, Wd = 0f; int perSide = 0; Vector2? spot = null;
            Rect RoundAt(Vector2 p, float l, float w) => alongA ? new Rect(p.x - l * 0.5f - 0.3f, p.y - w * 0.5f - 0.75f, l + 0.6f, w + 1.5f)
                                                                : new Rect(p.x - w * 0.5f - 0.75f, p.y - l * 0.5f - 0.3f, w + 1.5f, l + 0.6f);
            var offsets = new System.Collections.Generic.List<Vector2>();
            for (float oa = -4f; oa <= 4.01f; oa += 0.4f)
                for (float ob = -3f; ob <= 3.01f; ob += 0.4f) offsets.Add(new Vector2(oa, ob));
            offsets.Sort((p, q) => p.sqrMagnitude.CompareTo(q.sqrMagnitude));
            foreach (var (l, w, n) in new[] { (1.9f, 0.95f, 3), (1.3f, 0.85f, 2) })
            {
                foreach (var o in offsets)
                {
                    var p = r.r.center + ax * o.x + bx * o.y;
                    if (Free(r, RoundAt(p, l, w))) { spot = p; break; }
                }
                if (spot != null) { L = l; Wd = w; perSide = n; break; }
            }
            if (spot != null)
            {
                var tc = spot.Value;
                r.placed.Add(RoundAt(tc, L, Wd));
                var wood = own.NextDouble() < 0.5 ? _frameWood : _doorWood;
                var rot = Quaternion.LookRotation(Iw(bx));
                Vector3 c = f.P(tc.x, tc.y, r.y);
                m.Box(wood, c + Vector3.up * 0.74f, new Vector3(L, 0.04f, Wd), rot);
                foreach (float sa in new[] { -1f, 1f })
                    foreach (float sb in new[] { -1f, 1f })
                        m.Box(wood, c + Iw(ax) * (sa * (L * 0.5f - 0.1f)) + Iw(bx) * (sb * (Wd * 0.5f - 0.1f)) + Vector3.up * 0.36f, new Vector3(0.06f, 0.72f, 0.06f), rot);
                foreach (float sb in new[] { -1f, 1f })
                    for (int k = 0; k < perSide; k++)
                        PlaceTown(props, Prop("painted_wooden_chair_01"), c + Iw(ax) * ((k - (perSide - 1) * 0.5f) * 0.62f) + Iw(bx) * (sb * (Wd * 0.5f + 0.3f)),
                                  f.Yaw(bx * -sb) + Rand(own, -6f, 6f));
                PlaceTown(props, Prop("wooden_bowl_01"), c + Vector3.up * 0.76f, Rand(own, 0f, 360f), navBlock: false);
                string[] fruit = { "food_pomegranate_01", "food_apple_01", "food_lime_01" };
                PlaceTown(props, Prop(fruit[own.Next(fruit.Length)]), c + Vector3.up * 0.8f + Iw(ax) * 0.03f, Rand(own, 0f, 360f), navBlock: false);
            }
            Sideboard(m, det, props, f, r, own);
            if (own.NextDouble() < 0.7) PropAgainstWall(props, f, r, own, "wooden_display_shelves_01", 1.4f, 0.6f, true);
            FloorVase(props, f, r, own);
        }

        /// <summary>A sideboard (or a chest of drawers): a wooden box with its drawer seams, two or three things on top.</summary>
        private static void Sideboard(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            var spot = AgainstWall(r, own, 1.5f, 0.46f, false);
            if (spot == null) return;
            var (at, wall) = spot.Value;
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            Vector2 into = Inward(wall), along = new Vector2(-into.y, into.x);
            var rot = Quaternion.LookRotation(Iw(into));
            Vector3 c = f.P(at.x, at.y, r.y);
            var wood = own.NextDouble() < 0.5 ? _frameWood : own.NextDouble() < 0.5 ? _doorWood : _cabinetWhite;
            m.Box(wood, c + Vector3.up * 0.42f, new Vector3(1.5f, 0.84f, 0.45f), rot);
            m.Box(own.NextDouble() < 0.5 ? wood : _stallMarble, c + Vector3.up * 0.86f, new Vector3(1.54f, 0.03f, 0.48f), rot);
            Vector3 face = c + Iw(into) * 0.226f;
            Bar(det, _frameDark, face - Iw(along) * 0.74f + Vector3.up * 0.62f, face + Iw(along) * 0.74f + Vector3.up * 0.62f, Iw(into), 0.008f, 0.004f);
            foreach (float s in new[] { -0.25f, 0.25f })
                Bar(det, _frameDark, face + Iw(along) * s + Vector3.up * 0.06f, face + Iw(along) * s + Vector3.up * 0.62f, Iw(into), 0.008f, 0.004f);
            foreach (float s in new[] { -0.5f, 0f, 0.5f })
                m.Box(_frameAlu, face + Iw(along) * s + Iw(into) * 0.012f + Vector3.up * 0.7f, new Vector3(0.1f, 0.018f, 0.025f), rot);
            string[] onTop = { "ceramic_vase_01", "ceramic_vase_02", "brass_vase_01", "brass_pot_02", "brass_goblets", "vintage_oil_lamp", "carved_wooden_plate", "antique_ceramic_vase_01" };
            int n = own.Next(2, 4);
            for (int i = 0; i < n; i++)
                PlaceTown(props, Prop(onTop[own.Next(onTop.Length)]), c + Iw(along) * Rand(own, -0.6f, 0.6f) - Iw(into) * Rand(own, 0f, 0.1f) + Vector3.up * 0.875f,
                          Rand(own, 0f, 360f), navBlock: false);
        }

        /// <summary>A wardrobe: a wooden box two metres high with its doors' seam and handles, kept clear of windows.</summary>
        private static void Wardrobe(TownMesh m, TownMesh det, HouseFrame f, Room r, System.Random own)
        {
            var spot = AgainstWall(r, own, 1.3f, 0.6f, true);
            if (spot == null) return;
            var (at, wall) = spot.Value;
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            Vector2 into = Inward(wall), along = new Vector2(-into.y, into.x);
            var rot = Quaternion.LookRotation(Iw(into));
            Vector3 c = f.P(at.x, at.y, r.y);
            var wood = own.NextDouble() < 0.6 ? _doorWood : own.NextDouble() < 0.5 ? _frameWood : _cabinetWhite;
            m.Box(wood, c + Vector3.up * 1.0f, new Vector3(1.3f, 2.0f, 0.58f), rot);
            m.Box(wood, c + Vector3.up * 2.03f, new Vector3(1.36f, 0.06f, 0.62f), rot);                       // cornice
            Vector3 face = c + Iw(into) * 0.291f;
            Bar(det, _frameDark, face + Vector3.up * 0.06f, face + Vector3.up * 1.95f, Iw(into), 0.008f, 0.004f);
            Bar(det, _frameDark, face - Iw(along) * 0.64f + Vector3.up * 0.3f, face + Iw(along) * 0.64f + Vector3.up * 0.3f, Iw(into), 0.008f, 0.004f);
            foreach (float s in new[] { -0.06f, 0.06f })
                m.Box(_frameAlu, face + Iw(along) * s + Iw(into) * 0.015f + Vector3.up * 1.1f, new Vector3(0.02f, 0.18f, 0.03f), rot);
        }

        /// <summary>A bedside table each side of a bed's head, a lamp with a linen shade on each.</summary>
        private static void BedsideTables(TownMesh m, HouseFrame f, Room r, Vector2 at, int wall, System.Random own)
        {
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            Vector2 into = Inward(wall), side = new Vector2(-into.y, into.x);
            var rot = Quaternion.LookRotation(Iw(into));
            var wood = own.NextDouble() < 0.5 ? _frameWood : _doorWood;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector2 tc = at - into * 0.85f + side * (s * 1.18f);
                var foot = new Rect(tc.x - 0.24f, tc.y - 0.24f, 0.48f, 0.48f);
                if (!Free(r, foot)) continue;
                r.placed.Add(foot);
                Vector3 c = f.P(tc.x, tc.y, r.y);
                m.Box(wood, c + Vector3.up * 0.27f, new Vector3(0.45f, 0.54f, 0.4f), rot);
                m.Cylinder(_frameDark, c + Vector3.up * 0.54f, 0.06f, 0.28f, 10, true, false);
                m.Cylinder(_mattressMat, c + Vector3.up * 0.8f, 0.16f, 0.2f, 14, false, false, 0.11f);
            }
        }

        /// <summary>An upholstered armchair against a wall: seat, back and arms in one of the house's cloths.</summary>
        private static void Armchair(TownMesh m, HouseFrame f, Room r, System.Random own)
        {
            var spot = AgainstWall(r, own, 0.86f, 0.86f, false);
            if (spot == null) return;
            var (at, wall) = spot.Value;
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            Vector2 into = Inward(wall), side = new Vector2(-into.y, into.x);
            var rot = Quaternion.LookRotation(Iw(into));
            Vector3 c = f.P(at.x, at.y, r.y);
            var cloth = _cloth[own.Next(_cloth.Length)];
            m.Box(cloth, c + Vector3.up * 0.22f + Iw(into) * 0.04f, new Vector3(0.82f, 0.44f, 0.76f), rot);              // seat
            m.Box(cloth, c - Iw(into) * 0.33f + Vector3.up * 0.6f, new Vector3(0.82f, 0.72f, 0.18f), rot);                // back
            foreach (float s in new[] { -1f, 1f })
                m.Box(cloth, c + Iw(side) * (s * 0.35f) + Vector3.up * 0.56f + Iw(into) * 0.02f, new Vector3(0.14f, 0.24f, 0.8f), rot);   // arms
            m.Box(_mattressMat, c + Vector3.up * 0.47f + Iw(into) * 0.06f, new Vector3(0.54f, 0.06f, 0.6f), rot);        // cushion
        }

        /// <summary>A tall floor vase (glazed, painted or brass) against a wall. The succulent in a pot that was here stood a
        /// metre wide on the floor at any height a pot would hold: the scan is a ground-hugging clump.</summary>
        private static void FloorVase(Transform props, HouseFrame f, Room r, System.Random own)
        {
            var spot = AgainstWall(r, own, 0.45f, 0.45f, false);
            if (spot == null) return;
            var (at, _) = spot.Value;
            string[] vases = { "ceramic_vase_03", "ceramic_vase_04", "antique_ceramic_vase_01", "brass_vase_03", "ceramic_pot" };
            PlaceTown(props, Prop(vases[own.Next(vases.Length)]), f.P(at.x, at.y, r.y), Rand(own, 0f, 360f), Rand(own, 0.5f, 0.8f));
        }

        /// <summary>
        /// Curtains at a floor's windows: two pleated panels either side, from a rod above the head to just under the
        /// sill (clear of a sofa or a bed below), one cloth per house. The old town keeps its shutters.
        /// </summary>
        private static void Curtains(TownMesh det, Mass b, HouseFrame f, int fl, float y0, float y1, System.Random own)
        {
            if (b.oldStyle) return;
            var mat = _curtains[own.Next(_curtains.Length)];
            foreach (var o in b.openings.Where(o => o.floor == fl))
            {
                if (o.h.w - o.h.z > 2.05f || own.NextDouble() < 0.15) continue;            // doors; and now and then a bare window
                var span = DoorSpan(f, b, o.side, o.h);
                int wall = LocalWall(b, o.side);
                Vector2 into = Inward(wall), along = wall < 2 ? Vector2.right : Vector2.up;
                float line = wall == 0 ? 0f : wall == 1 ? f.Dp : wall == 2 ? 0f : f.W;
                Vector2 Pt(float s, float off) => wall < 2 ? new Vector2(s, line) + into * off : new Vector2(line, s) + into * off;
                Vector3 n = f.A * into.x + f.D * into.y, u = f.A * along.x + f.D * along.y;
                float top = Mathf.Min(y1 - 0.06f, b.wallBottom + o.h.w + 0.14f), bottom = Mathf.Max(y0 + 0.05f, b.wallBottom + o.h.z - 0.12f);
                if (top - bottom < 0.5f) continue;
                var rod0 = Pt(span.x - 0.4f, 0.1f); var rod1 = Pt(span.y + 0.4f, 0.1f);
                Bar(det, _frameDark, f.P(rod0.x, rod0.y, top + 0.03f), f.P(rod1.x, rod1.y, top + 0.03f), Vector3.up, 0.025f, 0.02f);
                foreach (float edge in new[] { span.x, span.y })
                {
                    float dir = edge == span.x ? -1f : 1f;
                    for (int k = 0; k < 3; k++)                        // three strips at two depths: the pleats
                    {
                        float s = edge + dir * (0.32f - k * 0.14f) - dir * 0.02f;
                        var p = Pt(s, k % 2 == 0 ? 0.085f : 0.115f);
                        Prism(det, mat, f.P(p.x, p.y, (top + bottom) * 0.5f), u, Vector3.up, n, 0.15f, top - bottom, 0.02f);
                    }
                }
            }
        }
    }
}
#endif
