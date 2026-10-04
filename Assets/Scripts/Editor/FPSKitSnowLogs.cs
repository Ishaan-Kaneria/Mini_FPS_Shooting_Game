#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What makes a chalet look built rather than modelled (2026-10-04, "use more and more triangles ...
    /// houses, cars, ground ... in detail"): walls laid as individual round logs with the corners
    /// notched and overhanging, a stone course under them, a roof laid in shingle rows with a ridge
    /// cap, fascia and rake boards, gable ends of vertical boards, a lumpy snow blanket with an icicle-
    /// heavy eave, and a balcony with turned balusters. All of it triangles; none of it a texture.
    ///
    /// The logs are render-only. The collision and the navigation bake read the thin core wall each
    /// builder still lays under them, which sits inside the logs.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float LogR = 0.19f;
        private const float LogPitch = LogR * 1.8f;
        private const float WallInset = 0.4f;   // where the enterable chalet's side-wall runs start, from the box corner

        /// <summary>
        /// Round logs along one wall run, a course every <see cref="LogPitch"/>, stopping short of any opening
        /// the course would cross. <paramref name="overA"/> and <paramref name="overB"/> are how far the first
        /// and last log of a course stick out past the corner; <paramref name="yShift"/> half-steps the whole
        /// run so front and side courses interlock at the corners.
        /// </summary>
        private static void LogRun(MeshBuild logs, Frame f, Vector2 a, Vector2 b, float y0, float height, float yShift,
                                   List<Opening> openings, float overA, float overB)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.3f) return;
            var dir = d / length;

            for (float y = y0 + LogR + yShift; y < height - 0.08f; y += LogPitch)
            {
                var cuts = new List<Vector2>();
                if (openings != null)
                    foreach (var o in openings)
                        if (y + LogR > o.Bottom && y - LogR < Mathf.Min(o.Top, height))
                            cuts.Add(new Vector2(o.At - o.Width * 0.5f, o.At + o.Width * 0.5f));
                cuts.Sort((p, q) => p.x.CompareTo(q.x));

                float s = 0f;
                foreach (var c in cuts)
                {
                    LogSeg(logs, f, a, dir, length, y, s, c.x, overA, overB);
                    s = Mathf.Max(s, c.y);
                }
                LogSeg(logs, f, a, dir, length, y, s, length, overA, overB);
            }
        }

        private static void LogSeg(MeshBuild logs, Frame f, Vector2 a, Vector2 dir, float length, float y, float s0, float s1,
                                   float overA, float overB)
        {
            if (s1 - s0 < 0.3f) return;
            float p0 = s0 - (s0 <= 0.01f ? overA : 0f);
            float p1 = s1 + (s1 >= length - 0.01f ? overB : 0f);
            var A = f.P(a.x + dir.x * p0, y, a.y + dir.y * p0);
            var B = f.P(a.x + dir.x * p1, y, a.y + dir.y * p1);
            logs.Tube(A, B, LogR, LogR, 9);
        }

        /// <summary>
        /// All four walls of a box in logs, interlocked at the corners. <paramref name="stairSide"/> (-1 none,
        /// else a face number) keeps that wall's corner logs from sticking out where a stair runs beside it.
        /// </summary>
        private static void LogBox(MeshBuild logs, Frame f, float cu, float cv, float w, float d, float y0, float height,
                                   List<Opening> front, List<Opening> back, List<Opening> left, List<Opening> right,
                                   bool noPlusUOverhang)
        {
            float hw = w * 0.5f - LogR, hd = d * 0.5f - LogR;
            const float over = 0.3f;
            float oEndU = noPlusUOverhang ? 0f : over + LogR;

            // Front and back run along u; their openings are measured from the -u corner of the box wall,
            // so shift the cut list by the inset the logs begin at.
            LogRun(logs, f, new Vector2(cu - hw, cv - hd), new Vector2(cu + hw, cv - hd), y0, height, 0f,
                   Shift(front, -LogR), over + LogR, oEndU);
            LogRun(logs, f, new Vector2(cu - hw, cv + hd), new Vector2(cu + hw, cv + hd), y0, height, 0f,
                   Shift(back, -LogR), over + LogR, oEndU);
            // Sides run along v and sit half a course higher.
            LogRun(logs, f, new Vector2(cu - hw, cv - hd), new Vector2(cu - hw, cv + hd), y0, height, LogPitch * 0.5f,
                   Shift(left, WallInset - LogR), over + LogR, over + LogR);
            LogRun(logs, f, new Vector2(cu + hw, cv - hd), new Vector2(cu + hw, cv + hd), y0, height, LogPitch * 0.5f,
                   Shift(right, WallInset - LogR), noPlusUOverhang ? 0f : over + LogR, noPlusUOverhang ? 0f : over + LogR);
        }

        private static List<Opening> Shift(List<Opening> list, float by)
        {
            if (list == null) return null;
            var shifted = new List<Opening>(list.Count);
            foreach (var o in list) shifted.Add(new Opening(o.At + by, o.Width, o.Bottom, o.Top));
            return shifted;
        }

        /// <summary>
        /// A course of dressed stone under the logs, 0.75 m high, broken for the doorways it is given.
        /// Laid with WallRun so it is the same wall the logs sit on.
        /// </summary>
        private static void StoneBase(Transform parent, int layer, Frame f, float cu, float cv, float w, float d,
                                      List<Opening> frontDoors, List<Opening> backDoors, bool plusUSide, List<Opening> rightDoors = null)
        {
            var stone = new MeshBuild { UVScale = 0.5f };
            const float t = 0.46f, h = 0.75f;
            float hw = w * 0.5f, hd = d * 0.5f;
            WallRun(stone, f, new Vector2(cu - hw - 0.05f, cv - hd + t * 0.5f - 0.1f), new Vector2(cu + hw + 0.05f, cv - hd + t * 0.5f - 0.1f), 0f, h, t, frontDoors);
            WallRun(stone, f, new Vector2(cu - hw - 0.05f, cv + hd - t * 0.5f + 0.1f), new Vector2(cu + hw + 0.05f, cv + hd - t * 0.5f + 0.1f), 0f, h, t, backDoors);
            WallRun(stone, f, new Vector2(cu - hw + t * 0.5f - 0.1f, cv - hd + t - 0.1f), new Vector2(cu - hw + t * 0.5f - 0.1f, cv + hd - t + 0.1f), 0f, h, t);
            WallRun(stone, f, new Vector2(cu + hw - t * 0.5f + 0.1f, cv - hd + t - 0.1f), new Vector2(cu + hw - t * 0.5f + 0.1f, cv + hd - t + 0.1f), 0f, h, t, rightDoors);
            _ = plusUSide;
            var go = MeshObject(parent, "StoneBase", ToMesh(stone, DenseKey("stonebase")), _snowStoneMat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Concrete", collider: false);
            NoStanding(go);
            Hide(go);
        }

        // ==================================================================
        // Roofs
        // ==================================================================
        /// <summary>
        /// A pitched roof over a w by d box, ridge along v: laid in shingle rows (each a thin board stepped
        /// proud of the one below), a ridge cap, fascia along the eaves, rake boards up both gables, and the
        /// gable ends filled with vertical boards (into <paramref name="gable"/>, which the caller gives the
        /// plank material). The snow blanket is a lumpy grid, not a plane, with a roll of snow along each eave.
        /// Returns the rise.
        /// </summary>
        private static float GableRoof(MeshBuild roof, MeshBuild snow, Frame f, float cu, float cv, float w, float d,
                                       float baseY, float pitch, float over, MeshBuild gable = null)
        {
            float hw = w * 0.5f + over, hd = d * 0.5f + over;
            float rise = hw * pitch;
            float angle = Mathf.Atan(pitch) * Mathf.Rad2Deg;
            float slopeLen = Mathf.Sqrt(hw * hw + rise * rise);
            var inside = f.P(cu, baseY + rise * 0.3f, cv);

            // ---- shingle rows, both slopes ----
            const float rowLen = 0.34f;
            int rows = Mathf.Max(3, Mathf.CeilToInt(slopeLen / rowLen));
            float rl = slopeLen / rows;
            foreach (float side in new[] { -1f, 1f })
            {
                var rot = f.Rot * Quaternion.Euler(0f, 0f, -side * angle);
                for (int i = 0; i < rows; i++)
                {
                    float s = (i + 0.5f) * rl;                        // along the slope from the eave
                    float u = side * (hw - s * Mathf.Cos(angle * Mathf.Deg2Rad));
                    float y = baseY + s * Mathf.Sin(angle * Mathf.Deg2Rad);
                    var centre = f.P(cu + u, y, cv) + rot * new Vector3(0f, 0.035f + (i % 2) * 0.018f, 0f);
                    roof.Box(centre, new Vector3(rl * 1.12f, 0.07f, d + over * 2f), rot);
                }
            }
            // Ridge cap: a diamond-section beam along the whole ridge.
            roof.Box(f.P(cu, baseY + rise + 0.07f, cv), new Vector3(0.34f, 0.34f, d + over * 2f + 0.12f), f.Rot * Quaternion.Euler(0f, 0f, 45f));

            if (gable != null)
            {
                // Fascia along each eave and a rake board up each gable edge.
                foreach (float side in new[] { -1f, 1f })
                {
                    var rot = f.Rot * Quaternion.Euler(0f, 0f, -side * angle);
                    gable.Box(f.P(cu + side * hw, baseY - 0.02f, cv), new Vector3(0.06f, 0.22f, d + over * 2f + 0.1f), f.Rot);
                    foreach (float end in new[] { -1f, 1f })
                    {
                        var mid = f.P(cu + side * hw * 0.5f, baseY + rise * 0.5f + 0.07f, cv + end * (hd + 0.02f));
                        gable.Box(mid, new Vector3(slopeLen + 0.1f, 0.2f, 0.07f), rot);
                    }
                }

                // The gable ends: vertical boards from the wall top to the roof line, each its own box.
                float wallHalf = w * 0.5f;
                int boards = Mathf.Max(6, Mathf.CeilToInt(w / 0.19f));
                float bw = w / boards;
                foreach (float end in new[] { -1f, 1f })
                    for (int i = 0; i < boards; i++)
                    {
                        float u = -wallHalf + (i + 0.5f) * bw;
                        float top = (hw - Mathf.Abs(u)) * pitch - 0.05f;
                        if (top < 0.05f) continue;
                        gable.Box(f.P(cu + u, baseY + top * 0.5f, cv + end * (d * 0.5f + 0.03f)), new Vector3(bw * 0.94f, top, 0.05f), f.Rot);
                    }
            }

            if (snow != null)
            {
                // A lumpy blanket: a grid over each slope lifted by a little noise, plus a roll along each eave.
                int nu = 5, nv = Mathf.Max(4, Mathf.CeilToInt(d / 1.1f));
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 Pt(int iu, int iv)
                    {
                        float tu = iu / (float)nu, tv = iv / (float)nv;
                        float s = Mathf.Lerp(0.4f, slopeLen - 0.05f, tu);
                        float u = side * (hw - s * Mathf.Cos(angle * Mathf.Deg2Rad));
                        float y = baseY + s * Mathf.Sin(angle * Mathf.Deg2Rad);
                        float v = Mathf.Lerp(-hd + 0.3f, hd - 0.3f, tv);
                        float lump = (Fbm2(u * 0.9f + cu, v * 0.9f + cv, 8803, 2) + 0.5f) * 0.2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(tu * 1.15f));
                        var n = new Vector3(side * Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad), 0f);
                        return f.P(cu + u, y, cv + v) + f.Axis(n) * (0.1f + lump);
                    }
                    for (int iu = 0; iu < nu; iu++)
                        for (int iv = 0; iv < nv; iv++)
                        {
                            AddOutward(snow, inside, Pt(iu, iv), Pt(iu, iv + 1), Pt(iu + 1, iv + 1));
                            AddOutward(snow, inside, Pt(iu, iv), Pt(iu + 1, iv + 1), Pt(iu + 1, iv));
                        }
                    var r0 = f.P(cu + side * (hw - 0.35f), baseY + 0.28f, cv - hd + 0.35f);
                    var r1 = f.P(cu + side * (hw - 0.35f), baseY + 0.28f, cv + hd - 0.35f);
                    snow.Tube(r0, r1, 0.17f, 0.17f, 7);
                }
            }
            return rise;
        }

        // ==================================================================
        // Balcony
        // ==================================================================
        /// <summary>
        /// A balcony across the front of the upper storey: a deck on brackets, a rail of turned balusters,
        /// corner posts. The deck is solid and kept off the bake (nobody can reach it).
        /// </summary>
        private static void Balcony(Transform parent, int layer, Frame f, float cu, float cv, float w, float d, float y)
        {
            var deck = new MeshBuild { UVScale = 0.5f };
            var rail = new MeshBuild { UVScale = 0.5f };
            float bw = w * 0.86f;
            var c = FaceAt(f, 0, cu, cv, w, d, 0f, y - 0.07f, 0.85f, out var rot);
            // Planks across the deck.
            int planks = Mathf.CeilToInt(1.7f / 0.14f);
            for (int i = 0; i < planks; i++)
                deck.Box(c + rot * new Vector3(0f, 0f, -0.85f + (i + 0.5f) * 1.7f / planks), new Vector3(bw, 0.07f, 1.7f / planks * 0.94f), rot);
            // Brackets under it.
            foreach (float s in new[] { -1f, -0.4f, 0.4f, 1f })
            {
                var b0 = FaceAt(f, 0, cu, cv, w, d, s * bw * 0.45f, y - 0.5f, 0.45f, out _);
                deck.Box(b0, new Vector3(0.1f, 0.1f, 1.0f), rot * Quaternion.Euler(-38f, 0f, 0f));
            }
            // Posts, top rail, bottom rail, balusters.
            var postY = y + 0.55f;
            foreach (float s in new[] { -1f, 1f })
            {
                rail.Box(FaceAt(f, 0, cu, cv, w, d, s * bw * 0.5f, postY, 1.65f, out _), new Vector3(0.12f, 1.1f, 0.12f), rot);
                // Side rails returning to the wall.
                rail.Box(FaceAt(f, 0, cu, cv, w, d, s * bw * 0.5f, y + 1.02f, 0.85f, out _), new Vector3(0.07f, 0.07f, 1.7f), rot);
                rail.Box(FaceAt(f, 0, cu, cv, w, d, s * bw * 0.5f, y + 0.2f, 0.85f, out _), new Vector3(0.05f, 0.05f, 1.7f), rot);
            }
            rail.Box(FaceAt(f, 0, cu, cv, w, d, 0f, y + 1.04f, 1.65f, out _), new Vector3(bw + 0.1f, 0.08f, 0.1f), rot);
            rail.Box(FaceAt(f, 0, cu, cv, w, d, 0f, y + 0.2f, 1.65f, out _), new Vector3(bw, 0.05f, 0.05f), rot);
            for (float s = -bw * 0.5f + 0.15f; s < bw * 0.5f; s += 0.17f)
            {
                var p = FaceAt(f, 0, cu, cv, w, d, s, y + 0.1f, 1.65f, out _);
                rail.Tube(p, p + Vector3.up * 0.92f, 0.02f, 0.02f, 4);
            }
            Solid(parent, "BalconyDeck", deck, _plankMat, layer, "Wood");
            Visual(parent, "BalconyRail", rail, _logDarkMat, layer);
        }
    }
}
#endif
