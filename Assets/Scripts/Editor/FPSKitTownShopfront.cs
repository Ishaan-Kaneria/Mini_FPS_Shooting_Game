#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's shop fronts built in depth (2026-10-21, after "the shops look like texture cards"):
    ///
    ///   * The storefront stands <see cref="ShopRecess"/> (0.35 m) behind the faces of the piers between the shops, so
    ///     each shop is set into the wall, with a line of shadow down its jambs and under its head.
    ///   * In each opening: the roller shutter's box under the head and its guides down the jambs, and on the line behind
    ///     them a shopfront (a polished stone stallriser, display glass and a glazed door in a frame) or an open front.
    ///     Every frame member stands <see cref="FrameProud"/> (5 cm) out of the glass or the shutter it holds.
    ///   * On the fascia over the opening the sign is a plate of its own, 10 cm off the wall on four brackets, under a
    ///     concrete hood that overhangs it and throws a line of shadow across its top.
    ///   * Between the shops and the flats over them, a stone cornice (<see cref="RetailCornice"/>); at the foot of every
    ///     modern face, a concrete foundation course where the pavement meets the building (<see cref="FoundationTrim"/>).
    ///
    /// Layered like the rest of the facade (FPSKitTownFacade): what has size and is seen from across the town -- the
    /// glass, the stallriser, the shutter and its box, the sign plate and its hood, the cornice, the foundation -- goes
    /// in the chunk mesh, which has a collider and takes part in the navmesh bake; the thin members -- frames, mullions,
    /// door stiles, the push bar, shutter guides and bottom bars, sign brackets, drips -- go in the detail mesh, which
    /// has none and is culled at a distance.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>How far the storefront stands behind the piers' faces.</summary>
        private const float ShopRecess = 0.35f;
        /// <summary>How far a frame stands out of the glass or shutter it holds.</summary>
        private const float FrameProud = 0.05f;

        private enum ShopState { Glazed, Stall, HalfShut, Shut }

        /// <summary>A street face of shops, for the cover set down outside them (FPSKitTownCover): its run along the wall
        /// face, its floor, and each opening's span along it and state.</summary>
        private sealed class ShopFace
        {
            public Vector3 a0, u, nrm;
            public float len, floorY;
            public readonly List<(float x0, float x1, ShopState state)> bays = new List<(float, float, ShopState)>();
        }
        private static readonly List<ShopFace> _shopFaces = new List<ShopFace>();

        private static Material _foundation, _stallriser, _shopGlass, _shopInside;

        private static void ResolveShopfrontMaterials()
        {
            // A cast-concrete grey, darker than any wall so the base reads as what the building stands on (the render
            // texture tinted: its mean is 187, 177, 163; this gives about 129, 126, 122). Not concrete_wall_008: green.
            _foundation = TownMat("FoundationConcrete", "plastered_wall_02", new Color(0.69f, 0.71f, 0.75f), 1.6f, 0.2f);
            // The stallriser under the display glass: polished dark granite, as nearly every shopfront here has.
            _stallriser = TownFlat("StallGranite", new Color(0.13f, 0.125f, 0.12f), 0.72f, 0.05f);
            // Shop glass: alpha-blended with its reflections faded by its alpha, so the street is reflected faintly and
            // the shop shows through. The windows' glass (premultiplied, reflections at full strength) suits a window
            // with a dark room behind it; across a shopfront it reflected the bright street over every pane, a milky grey.
            _shopGlass = TownFlat("ShopGlass", new Color(0.20f, 0.24f, 0.25f, 0.34f), 0.94f, 0f, transparent: true);
            _shopGlass.SetFloat("_Blend", 0f);
            _shopGlass.SetFloat("_BlendModePreserveSpecular", 0f);
            _shopGlass.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _shopGlass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _shopGlass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _shopGlass.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            _shopGlass.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            UnityEditor.EditorUtility.SetDirty(_shopGlass);
            // Inside an open shop: painted walls in its own light, darker than the street, not the black of a shut room.
            _shopInside = TownFlat("ShopInsideLit", new Color(0.34f, 0.32f, 0.29f), 0.25f);
        }

        /// <summary>The height of a shop opening. Above it: the fascia, with the sign under its hood, then the cornice
        /// (a 4 m ground floor keeps 2.7 m, a 4.2 m one 2.9).</summary>
        private static float ShopOpeningH(Mass b) => Mathf.Min(2.9f, b.groundH - 1.3f);

        /// <summary>How high the fascia over the shops goes above the floor: to the cornice's foot, or under the roof's
        /// band on a single-storey row.</summary>
        private static float FasciaTop(Mass b) => b.floors >= 2 ? b.groundH - 0.53f : b.groundH - 0.42f;

        // ------------------------------------------------------------------
        // The shop in its opening
        // ------------------------------------------------------------------
        /// <summary>
        /// One shop in its opening (reveals <see cref="ShopRecess"/> deep already cut): open behind glass, open to the
        /// street, half shut or shut; the sign over it and, on some, an awning. <paramref name="p0"/>, <paramref name="p1"/>
        /// are the opening's bottom corners on the wall face, <paramref name="q0"/> its top left.
        ///
        /// It makes the same draws from the building's random sequence, in the same order, as the flat shop front it
        /// replaced (state, shutter, goods, sign, awning), so every building after it in the town comes out as before;
        /// what is new (glass or not, the door's place, the frame's finish, how far a shutter is down) comes from a
        /// sequence of its own, seeded by where it stands.
        /// </summary>
        private static ShopState ShopFront(TownMesh m, TownMesh det, Mass b, Vector3 p0, Vector3 p1, Vector3 q0, Vector3 nrm, Transform props)
        {
            var rng = b.rng;
            var own = new System.Random(Mathf.RoundToInt(p0.x * 73f) * 7919 ^ Mathf.RoundToInt(p0.z * 37f) * 104729);
            Vector3 u = (p1 - p0).normalized, up = Vector3.up;
            float W = Vector3.Distance(p0, p1), H = q0.y - p0.y;
            Vector3 o = p0 - nrm * ShopRecess;                         // the storefront's line, at the floor
            Vector3 S(float x, float y, float off = 0f) => o + u * x + up * y + nrm * off;

            double pick = rng.NextDouble();                            // < .35 open, < .55 half shut, else shut
            var state = pick < 0.35 ? (own.NextDouble() < 0.6 ? ShopState.Glazed : ShopState.Stall)
                      : pick < 0.55 ? ShopState.HalfShut : ShopState.Shut;
            const float box = 0.36f;                                   // the shutter's box, inside the head
            float hs = H - box;                                        // under it: the shutter's travel, the shopfront
            // Out from the storefront line: frame fronts +0.05, the shutter's curtain +0.10, its guides' fronts +0.15,
            // the box's front +0.25 (still 0.10 behind the piers).
            const float curtain = 0.10f;

            // ---- the shop behind (nothing of it shows behind a shut shutter) ----
            Material shutter = null;
            if (state != ShopState.Shut)
            {
                Vector3 back = -nrm * 3.2f, a = S(0f, 0f), c = S(W, 0f), top = up * hs;
                // Behind a half-shut shutter the shop is closed, its light off; an open one is lit.
                var inside = state == ShopState.HalfShut ? _shopDark : _shopInside;
                m.Quad(_terrazzo, a, c, c + back, a + back, up);
                m.Quad(inside, a + back, c + back, c + back + top, a + back + top, nrm);
                m.Quad(inside, a, a + back, a + back + top, a + top, u);
                m.Quad(inside, c, c + back, c + back + top, c + top, -u);
                m.Quad(_shopDark, a + top, c + top, c + back + top, a + back + top, Vector3.down);
                if (state == ShopState.HalfShut) shutter = rng.NextDouble() < 0.4 ? _shutterRust : _shutterPaint;
                else
                {
                    var centre = (p0 + p1) * 0.5f;
                    _shopFronts.Add(new Vector4(centre.x + nrm.x * 1.2f, centre.y, centre.z + nrm.z * 1.2f, Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg));
                    // Shelves inside, facing out; a box or a crate on the floor.
                    PlaceTown(props, Prop("steel_frame_shelves_01"), S(W * 0.25f, 0f) + back * 0.85f, Mathf.Atan2(-nrm.x, -nrm.z) * Mathf.Rad2Deg + 180f, 2.0f);
                    PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "cardboard_box_01" : "plastic_crate_02"), S(W * 0.75f, 0f) + back * 0.5f, (float)rng.NextDouble() * 360f, 0.45f);
                }
            }
            else shutter = rng.NextDouble() < 0.4 ? _shutterRust : _shutterPaint;

            // ---- the roller shutter: its box under the head, its guides down the jambs, its curtain and bottom bar ----
            Prism(m, _shutterBoxMat, S(W * 0.5f, hs + box * 0.5f, 0.25f), u, up, nrm, W, box, 0.25f);
            foreach (float x in new[] { 0.035f, W - 0.035f })
                Bar(det, _shutterBoxMat, S(x, 0f, curtain + FrameProud), S(x, hs, curtain + FrameProud), nrm, 0.07f, 0.09f);
            float bottom = state == ShopState.Shut ? 0f
                         : state == ShopState.HalfShut ? hs * Mathf.Lerp(0.35f, 0.62f, (float)own.NextDouble())
                         : hs;                                          // rolled up: the bottom bar under the box
            if (shutter != null)
                m.Quad(shutter, S(0.07f, bottom, curtain), S(W - 0.07f, bottom, curtain), S(W - 0.07f, hs, curtain), S(0.07f, hs, curtain), nrm);
            Prism(det, _shutterBoxMat, S(W * 0.5f, Mathf.Max(0.04f, bottom - 0.04f), curtain + FrameProud), u, up, nrm, W - 0.14f, 0.08f, FrameProud + 0.03f);

            // ---- behind the shutter's line: a shopfront in its frame, or the open shop ----
            if (state == ShopState.Glazed || state == ShopState.HalfShut)
            {
                double finish = own.NextDouble();
                var frame = finish < 0.5 ? _frameAlu : finish < 0.75 ? _frameBronze : _frameCharcoal;
                Shopfront(m, det, frame, o, u, nrm, W, hs, own);
            }
            else if (state == ShopState.Stall)
                // An open front: a stone threshold at the line, the shop open to the street.
                Prism(m, _stallriser, S(W * 0.5f, 0.02f, 0.06f), u, up, nrm, W, 0.04f, 0.12f);

            // ---- the sign on the fascia, and an awning on some ----
            ShopSign(m, det, rng, p0, u, nrm, W, H, FasciaTop(b));
            if (rng.NextDouble() < 0.35)
            {
                int k = rng.Next(4);
                var uv0 = new Vector2((k % 2) * 0.5f, (k / 2) * 0.5f);
                float d = 1.3f, drop = 0.6f;
                // Hung from the fascia just over the opening, under the sign plate's foot.
                Vector3 top0 = p0 + up * (H + 0.02f) + nrm * 0.05f, top1 = p1 + up * (H + 0.02f) + nrm * 0.05f;
                Vector3 bot0 = top0 + nrm * d - up * drop, bot1 = top1 + nrm * d - up * drop;
                var an = Vector3.Cross(top1 - top0, bot0 - top0).normalized; if (an.y < 0f) an = -an;
                m.QuadUV(_awningMat, top0, top1, bot1, bot0, an, uv0, uv0 + new Vector2(0.5f, 0f), uv0 + new Vector2(0.5f, 0.5f), uv0 + new Vector2(0f, 0.5f));
                m.QuadUV(_awningMat, top0, top1, bot1, bot0, -an, uv0, uv0 + new Vector2(0.5f, 0f), uv0 + new Vector2(0.5f, 0.5f), uv0 + new Vector2(0f, 0.5f));
            }
            return state;
        }

        /// <summary>
        /// A shopfront on the storefront line <paramref name="o"/> (its foot, left end), <paramref name="W"/> wide and
        /// <paramref name="hs"/> high: a glazed door a metre wide at one end or in the middle, display glass over a granite
        /// stallriser the rest of the width, a transom at the door's head with a fanlight over it; and its frame --
        /// jambs, head, sill, transom, mullions, the door's frame and its leaf's stiles and rails, standing
        /// <see cref="FrameProud"/> out of the glass -- with a push bar on the door.
        /// </summary>
        private static void Shopfront(TownMesh m, TownMesh det, Material frame, Vector3 o, Vector3 u, Vector3 nrm, float W, float hs, System.Random rng)
        {
            Vector3 up = Vector3.up;
            Vector3 S(float x, float y, float off = 0f) => o + u * x + up * y + nrm * off;
            const float riser = 0.45f, fw = 0.07f;
            float doorW = W >= 2.3f ? 1.0f : 0f;
            int at = rng.Next(3);
            float d0 = at == 0 ? 0.3f : at == 2 ? W - 0.3f - doorW : (W - doorW) * 0.5f, d1 = d0 + doorW;
            float doorH = Mathf.Min(2.2f, hs - 0.2f);
            float transom = doorH + fw * 0.5f;

            // Glass, the stallriser 3 cm proud of it, a fanlight the whole width.
            var spans = new List<Vector2>();
            if (doorW <= 0f) spans.Add(new Vector2(0f, W));
            else
            {
                if (d0 > 0.05f) spans.Add(new Vector2(0f, d0));
                if (W - d1 > 0.05f) spans.Add(new Vector2(d1, W));
            }
            foreach (var sp in spans)
            {
                Prism(m, _stallriser, S((sp.x + sp.y) * 0.5f, riser * 0.5f, 0.03f), u, up, nrm, sp.y - sp.x, riser, 0.15f);
                m.Quad(_shopGlass, S(sp.x, riser), S(sp.y, riser), S(sp.y, transom), S(sp.x, transom), nrm);
            }
            m.Quad(_shopGlass, S(0f, transom), S(W, transom), S(W, hs), S(0f, hs), nrm);
            if (doorW > 0f)
            {
                m.Quad(_shopGlass, S(d0 + 0.05f, 0.32f), S(d1 - 0.05f, 0.32f), S(d1 - 0.05f, doorH - 0.05f), S(d0 + 0.05f, doorH - 0.05f), nrm);
                Prism(m, frame, S((d0 + d1) * 0.5f, 0.16f, 0.02f), u, up, nrm, doorW - 0.1f, 0.32f, 0.04f);   // the kick panel
            }

            // The frame: 7 cm members, 5 cm out of the glass and 4 cm into the shop behind it.
            float depth = FrameProud + 0.04f;
            void Member(float x0, float y0, float x1, float y1, float t) => Bar(det, frame, S(x0, y0, FrameProud), S(x1, y1, FrameProud), nrm, t, depth);
            Member(fw * 0.5f, 0f, fw * 0.5f, hs, fw);
            Member(W - fw * 0.5f, 0f, W - fw * 0.5f, hs, fw);
            Member(0f, hs - fw * 0.5f, W, hs - fw * 0.5f, fw);
            Member(0f, transom, W, transom, fw);
            foreach (var sp in spans)
            {
                Member(sp.x, riser + fw * 0.5f, sp.y, riser + fw * 0.5f, fw);
                int lights = Mathf.Max(1, Mathf.RoundToInt((sp.y - sp.x) / 1.3f));
                for (int i = 1; i < lights; i++)
                {
                    float x = Mathf.Lerp(sp.x, sp.y, i / (float)lights);
                    Member(x, riser, x, transom, 0.05f);
                }
            }
            if (doorW <= 0f) return;
            Member(d0, 0f, d0, transom, fw);
            Member(d1, 0f, d1, transom, fw);
            // The leaf: stiles, a top rail and the kick panel's rail, a little proud of its glass; a push bar on stand-offs.
            const float leaf = 0.035f;
            Bar(det, frame, S(d0 + 0.08f, 0f, leaf), S(d0 + 0.08f, doorH, leaf), nrm, 0.09f, 0.05f);
            Bar(det, frame, S(d1 - 0.08f, 0f, leaf), S(d1 - 0.08f, doorH, leaf), nrm, 0.09f, 0.05f);
            Bar(det, frame, S(d0, doorH - 0.05f, leaf), S(d1, doorH - 0.05f, leaf), nrm, 0.09f, 0.05f);
            Bar(det, frame, S(d0, 0.34f, leaf), S(d1, 0.34f, leaf), nrm, 0.07f, 0.05f);
            Bar(det, _frameAlu, S(d0 + 0.17f, 1.05f, 0.11f), S(d1 - 0.17f, 1.05f, 0.11f), nrm, 0.035f, 0.035f);
            foreach (float x in new[] { d0 + 0.2f, d1 - 0.2f })
                Prism(det, _frameAlu, S(x, 1.05f, 0.11f), u, up, nrm, 0.025f, 0.025f, 0.08f);
        }

        /// <summary>
        /// A shop's sign on the fascia over its opening: its own plate (aluminium-edged, 5 cm thick) standing 10 cm off the
        /// wall on four brackets, under a concrete hood that overhangs the plate by 11 cm and throws a line of shadow
        /// across its top, with a drip under the hood's edge. As big as the fascia allows (4:1, like the sign atlas).
        /// </summary>
        private static void ShopSign(TownMesh m, TownMesh det, System.Random rng, Vector3 p0, Vector3 u, Vector3 nrm, float W, float H, float fasciaTop)
        {
            Vector3 up = Vector3.up;
            Vector3 F(float x, float y, float off = 0f) => p0 + u * x + up * y + nrm * off;
            const float off = 0.10f, plate = 0.05f, margin = 0.03f, gap = 0.03f, hood = 0.12f, reach = 0.26f;
            // Over the head, the plate (its margin round the lettering), the gap, the hood, and 3 cm clear of the cornice.
            float room = fasciaTop - (H + 0.06f) - margin - gap - hood - 0.03f;
            float sh = Mathf.Clamp(room, 0.3f, 0.75f), sw = sh * 4f;
            if (sw > W + 0.2f) { sw = W + 0.2f; sh = sw * 0.25f; }
            float cx = W * 0.5f, cy = H + 0.06f + sh * 0.5f;
            var cell = SignCell(ShopSigns[rng.Next(ShopSigns.Length)]);

            // The plate, and the sign on its face.
            Prism(m, _frameAlu, F(cx, cy, off + plate), u, up, nrm, sw + margin * 2f, sh + margin * 2f, plate);
            Vector3 s0 = F(cx - sw * 0.5f, cy - sh * 0.5f, off + plate + 0.002f), s1 = F(cx + sw * 0.5f, cy - sh * 0.5f, off + plate + 0.002f);
            m.QuadUV(_townSignMat, s0, s1, s1 + up * sh, s0 + up * sh, nrm,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            // Four brackets holding it off the wall.
            foreach (float x in new[] { cx - sw * 0.5f + 0.15f, cx + sw * 0.5f - 0.15f })
                foreach (float y in new[] { cy - sh * 0.5f + 0.08f, cy + sh * 0.5f - 0.08f })
                    Prism(det, _steel, F(x, y, off), u, up, nrm, 0.04f, 0.04f, off);
            // The hood, and its drip.
            float hw = sw + 0.3f, hy = cy + sh * 0.5f + margin + gap + hood * 0.5f;
            Prism(m, _band, F(cx, hy, reach), u, up, nrm, hw, hood, reach);
            Bar(det, _band, F(cx - hw * 0.5f, hy - hood * 0.5f - 0.015f, reach - 0.012f), F(cx + hw * 0.5f, hy - hood * 0.5f - 0.015f, reach - 0.012f), nrm, 0.03f, 0.024f);
        }

        // ------------------------------------------------------------------
        // Cornice and foundation
        // ------------------------------------------------------------------
        /// <summary>
        /// The cornice between the shops and the flats over them: an architrave, a bed moulding, a corona standing 0.34 m
        /// out with a drip under its edge, and a weathered top falling outward -- the building's trim stone on the masonry
        /// blocks, its coping concrete on the plaster ones. In the chunk: it is seen from across the town, and by afternoon
        /// it throws the fascia and the signs into shade. Its top is 10 cm under the first floor, so a balcony's slab sits
        /// on it. It runs on past a free corner by its own projection, and stops flush against a neighbour.
        /// </summary>
        private static void RetailCornice(TownMesh shell, TownMesh det, Mass b, Vector3 a0, Vector3 b0, Vector3 nrm, int side, bool freeStart, bool freeEnd)
        {
            float len = Vector3.Distance(a0, b0);
            if (len < 0.3f) return;
            Vector3 u = (b0 - a0) / len, up = Vector3.up;
            var rot = Quaternion.LookRotation(nrm);
            Material t = b.trim;
            float top = b.FloorY(1) - 0.10f;
            bool s0 = freeStart && b.faces[StartNeighbour(side)] != FaceKind.Blank;
            bool s1 = freeEnd && b.faces[EndNeighbour(side)] != FaceKind.Blank;
            void Piece(float y0, float h, float proud)
            {
                float e0 = s0 ? proud : 0f, e1 = s1 ? proud : 0f;
                shell.Box(t, a0 + u * ((len + e1 - e0) * 0.5f) + up * (y0 + h * 0.5f) + nrm * (proud * 0.5f),
                          new Vector3(len + e0 + e1, h, proud), rot);
            }
            const float corona = 0.34f;
            Piece(top - 0.43f, 0.14f, 0.04f);          // architrave
            Piece(top - 0.29f, 0.10f, 0.14f);          // bed moulding
            Piece(top - 0.19f, 0.16f, corona);         // corona
            // The weathered top, from the corona's front edge up to the wall, and its ends at a free corner.
            Vector3 l = a0 - u * (s0 ? corona : 0f), r = b0 + u * (s1 ? corona : 0f);
            Vector3 lf = l + nrm * corona + up * (top - 0.03f), rf = r + nrm * corona + up * (top - 0.03f);
            Vector3 lb = l + up * (top + 0.03f), rb = r + up * (top + 0.03f);
            var slope = Vector3.Cross(rf - lf, lb - lf).normalized; if (slope.y < 0f) slope = -slope;
            shell.Quad(t, lf, rf, rb, lb, slope);
            if (s0) shell.Tri(t, l + up * (top - 0.03f), lf, lb, -u);
            if (s1) shell.Tri(t, r + up * (top - 0.03f), rb, rf, u);
            // The drip: a downstand under the corona's front edge.
            Bar(det, t, l + nrm * (corona - 0.03f) + up * (top - 0.205f), r + nrm * (corona - 0.03f) + up * (top - 0.205f), nrm, 0.03f, 0.02f);
        }

        /// <summary>
        /// The concrete foundation course along a face: from below the ground to 0.45 m over the floor, 9 cm proud of the
        /// wall, its top weathered (falling outward), broken at every opening that comes down to the floor (and at an
        /// outside stair), run on past a free corner so the two faces' courses meet. <paramref name="gaps"/> are spans
        /// along the face from <paramref name="a0"/>.
        /// </summary>
        private static void FoundationTrim(TownMesh m, Mass b, Vector3 a0, Vector3 u, float len, Vector3 nrm, int side, float bottom, List<Vector2> gaps)
        {
            const float rise = 0.45f, proud = 0.09f, wash = 0.035f;
            Vector3 up = Vector3.up, o = nrm * proud;
            float top = b.baseY + rise, yf = top - wash;
            bool freeStart = b.faces[StartNeighbour(side)] != FaceKind.Blank, freeEnd = b.faces[EndNeighbour(side)] != FaceKind.Blank;
            void Run(float x0, float x1)
            {
                if (x0 <= 0.001f && freeStart) x0 = -proud;
                if (x1 >= len - 0.001f && freeEnd) x1 = len + proud;
                if (x1 - x0 < 0.08f) return;
                Vector3 w0 = a0 + u * x0, w1 = a0 + u * x1;
                m.Quad(_foundation, w0 + o + up * bottom, w1 + o + up * bottom, w1 + o + up * yf, w0 + o + up * yf, nrm);
                // The weathered top: from the front's edge up to the wall.
                var wn = Vector3.Cross(w1 - w0, w0 + up * top - (w0 + o + up * yf)).normalized; if (wn.y < 0f) wn = -wn;
                m.Quad(_foundation, w0 + o + up * yf, w1 + o + up * yf, w1 + up * top, w0 + up * top, wn);
                m.Quad(_foundation, w0 + up * bottom, w0 + o + up * bottom, w0 + o + up * yf, w0 + up * top, -u);
                m.Quad(_foundation, w1 + o + up * bottom, w1 + up * bottom, w1 + up * top, w1 + o + up * yf, u);
            }
            // Under an opening it rises only to the floor: a step's nosing at the threshold, the wall below a raised
            // shop floor in concrete like the rest of the base.
            void Under(float x0, float x1)
            {
                if (x1 - x0 < 0.08f || b.baseY - bottom < 0.05f) return;
                Vector3 w0 = a0 + u * x0, w1 = a0 + u * x1;
                m.Quad(_foundation, w0 + o + up * bottom, w1 + o + up * bottom, w1 + o + up * b.baseY, w0 + o + up * b.baseY, nrm);
                m.Quad(_foundation, w0 + o + up * b.baseY, w1 + o + up * b.baseY, w1 + up * b.baseY, w0 + up * b.baseY, up);
            }
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            float from = 0f;
            foreach (var g in gaps)
            {
                Run(from, Mathf.Min(g.x, len));
                Under(Mathf.Max(from, g.x), Mathf.Min(g.y, len));
                from = Mathf.Max(from, g.y);
            }
            Run(from, len);
        }

        /// <summary>The face that meets <paramref name="side"/> at its start corner (FaceRun's a), and at its end (b).</summary>
        private static int StartNeighbour(int side) => side == 0 ? 2 : side == 1 ? 3 : side == 2 ? 1 : 0;
        private static int EndNeighbour(int side) => side == 0 ? 3 : side == 1 ? 2 : side == 2 ? 0 : 1;

        /// <summary>A door's frame in its opening: jambs and head standing <see cref="FrameProud"/> out of the leaf, which
        /// stands <paramref name="depth"/> back in the reveal.</summary>
        private static void DoorFrame(TownMesh det, Material mat, Vector3 p0, Vector3 p1, float H, float depth, Vector3 nrm)
        {
            Vector3 u = (p1 - p0).normalized, up = Vector3.up, at = nrm * (FrameProud - depth);
            const float t = 0.08f;
            float d = FrameProud + 0.04f;
            Bar(det, mat, p0 + u * (t * 0.5f) + at, p0 + u * (t * 0.5f) + up * H + at, nrm, t, d);
            Bar(det, mat, p1 - u * (t * 0.5f) + at, p1 - u * (t * 0.5f) + up * H + at, nrm, t, d);
            Bar(det, mat, p0 + up * (H - t * 0.5f) + at, p1 + up * (H - t * 0.5f) + at, nrm, t, d);
        }
    }
}
#endif
