#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's facades as masonry is really built (the user's references, 2026-10-21: buff brick with arched,
    /// keystoned windows; a cut-stone block with sills, string courses, a bracketed cornice and a rusticated base).
    /// Iraqi and Levantine towns build exactly so -- Baghdad in buff brick, Amman and Jerusalem in dressed limestone --
    /// so the three styles sit in a Middle East town: buff brick, cut limestone and (the modern blocks) plaster.
    ///
    /// What makes a wall read as real at a few metres is depth and a crisp edge: windows set 0.3 m back in their
    /// reveals, joinery with its own thickness (frame, sashes, glazing bars, fanlights), a heavy stone sill proud of the
    /// face, a head (a brick arch ring with a stone keystone, a lintel, a hood), string courses at the floors, a cornice
    /// with dentils and brackets, quoins at the corners, and sharp textures at their true size.
    ///
    /// The fine work goes into a second mesh per 64 m chunk (<see cref="Detail"/>): no collider, left out of the
    /// navmesh bake, and culled past about 180 m, where none of it is more than a pixel. The walls, reveals, glass, string
    /// courses and cornices stay in the chunk itself.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private enum FacadeStyle { Plaster, Brick, Ashlar }

        private static Material _brickBuff, _brickSand, _brickArch, _ashlar, _ashlarPale, _rustic, _rusticDressed, _trimStone;
        private static Material _frameBronze, _frameCharcoal, _frameGreen, _frameWood, _iron, _shutterBoxMat;
        private static readonly Dictionary<Vector2Int, TownMesh> _details = new Dictionary<Vector2Int, TownMesh>();

        private static void ResolveFacadeMaterials()
        {
            // Tiles from the courses counted in each image (Poly Haven's stated sizes are off: brick_wall_003 says 4 m and
            // holds 16 courses, which made 25 cm bricks): a brick course 75 mm, a cut-stone course 30 cm. Tints are the
            // wanted colour over the image's mean -- buff brick about (195, 160, 110), limestone (205, 190, 160) -- so
            // the walls are the colour of the references, not the image's grey-brown (or, tinted by eye, orange).
            _brickBuff = TownMat("BrickBuff", "brick_wall_003", new Color(1.31f, 1.29f, 1.08f), 1.2f, 0.25f);
            _brickSand = TownMat("BrickSand", "brown_brick_02", new Color(1.10f, 1.15f, 1.13f), 0.62f, 0.25f);
            _brickArch = TownMat("BrickArch", "brick_wall_003", new Color(1.19f, 1.06f, 0.90f), 1.2f, 0.25f);
            _ashlar = TownMat("AshlarLimestone", "seaworn_sandstone_brick", new Color(1.47f, 1.58f, 1.65f), 2.4f, 0.25f);
            _ashlarPale = TownMat("AshlarHoney", "seaworn_sandstone_brick", new Color(1.47f, 1.43f, 1.30f), 2.4f, 0.25f);
            _rustic = TownMat("RusticStone", "sandstone_blocks_05", new Color(1.10f, 1.11f, 1.08f), 3f, 0.25f);
            _rusticDressed = TownMat("RusticDressed", "concrete_brick_wall_001", new Color(1.30f, 1.30f, 1.25f), 3f, 0.25f);
            _trimStone = TownMat("TrimStone", "plastered_wall_02", new Color(1.19f, 1.20f, 1.18f), 2.23f, 0.25f);
            _frameBronze = TownFlat("FrameBronze", new Color(0.11f, 0.09f, 0.075f), 0.45f, 0.35f);
            _frameCharcoal = TownFlat("FrameCharcoal", new Color(0.075f, 0.08f, 0.085f), 0.4f, 0.3f);
            _frameGreen = TownFlat("FrameGreen", new Color(0.06f, 0.15f, 0.11f), 0.4f);
            _frameWood = TownMat("FrameWood", "rough_pine_door", new Color(0.62f, 0.42f, 0.28f), 1.2f, 0.3f);
            _iron = TownFlat("WroughtIron", new Color(0.045f, 0.045f, 0.05f), 0.35f, 0.55f);
            _shutterBoxMat = TownFlat("ShutterBox", new Color(0.78f, 0.76f, 0.72f), 0.3f);
        }

        /// <summary>The detail mesh for whatever stands at <paramref name="p"/> (its 64 m chunk, like <see cref="Chunk"/>).</summary>
        private static TownMesh Detail(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / 64f), Mathf.FloorToInt(p.y / 64f));
            if (!_details.TryGetValue(key, out var m)) { m = new TownMesh(); _details[key] = m; }
            return m;
        }

        /// <summary>
        /// The detail meshes: no collider, out of the navmesh bake, culled when the chunk is small on screen, and drawn
        /// with copies of their materials that have no normal map, so the meshes carry no tangents. (With them, and
        /// joinery everywhere, the detail took 864 MB in the editor and Unity was killed for memory entering play.)
        /// </summary>
        private static void BuildDetailChunks(Transform group, int layer)
        {
            var plain = new Dictionary<Material, Material>();
            foreach (var kv in _details)
            {
                foreach (var mat in new List<Material>(kv.Value.Materials))
                    if (mat != null && mat.IsKeywordEnabled("_NORMALMAP")) kv.Value.Swap(mat, PlainCopy(mat, plain));
                var go = kv.Value.Build(group, $"Detail_{kv.Key.x}_{kv.Key.y}", layer, "Concrete", collider: false,
                                        compression: UnityEditor.ModelImporterMeshCompression.Medium, tangents: false);
                if (go == null) continue;
                var mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                mod.ignoreFromBuild = true;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.3f, new Renderer[] { go.GetComponent<Renderer>() }) });
                lod.RecalculateBounds();
            }
        }

        /// <summary>A copy of <paramref name="m"/> without its normal map (and its mask), saved beside it as *_Plain.</summary>
        private static Material PlainCopy(Material m, Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(m, out var c)) return c;
            string path = UnityEditor.AssetDatabase.GetAssetPath(m);
            string plainPath = string.IsNullOrEmpty(path) ? null : path.Replace(".mat", "_Plain.mat");
            c = plainPath != null ? UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(plainPath) : null;
            bool create = c == null;
            if (create) c = new Material(m);
            else c.CopyPropertiesFromMaterial(m);
            c.SetTexture("_BumpMap", null); c.DisableKeyword("_NORMALMAP");
            if (create && plainPath != null) UnityEditor.AssetDatabase.CreateAsset(c, plainPath);
            else if (plainPath != null) UnityEditor.EditorUtility.SetDirty(c);
            cache[m] = c;
            return c;
        }

        /// <summary>Gives a new (not old-town) mass its style: wall, trim, base, joinery, arches.</summary>
        private static void ApplyFacadeStyle(Mass b)
        {
            var rng = b.rng;
            double p = rng.NextDouble();
            b.style = p < 0.36 ? FacadeStyle.Ashlar : p < 0.66 ? FacadeStyle.Brick : FacadeStyle.Plaster;
            switch (b.style)
            {
                case FacadeStyle.Brick:
                    b.wall = rng.NextDouble() < 0.85 ? _brickBuff : _brickSand;
                    b.trim = _trimStone;
                    b.reveal = 0.32f;
                    b.arches = rng.NextDouble() < 0.75 ? 1 : 0;          // segmental heads on every floor
                    b.roundTop = rng.NextDouble() < 0.4;                 // and round ones on the top floor
                    b.joinery = rng.NextDouble() < 0.55 ? 0 : 1;
                    b.frame = rng.NextDouble() < 0.5 ? _frameBronze : rng.NextDouble() < 0.5 ? _frameCharcoal : _frameWhite;
                    break;
                case FacadeStyle.Ashlar:
                    b.wall = rng.NextDouble() < 0.65 ? _ashlar : _ashlarPale;
                    b.trim = _trimStone;
                    b.reveal = 0.3f;
                    b.arches = 0;
                    b.roundTop = rng.NextDouble() < 0.5;
                    b.hood = rng.NextDouble() < 0.7;
                    b.joinery = rng.NextDouble() < 0.45 ? 1 : rng.NextDouble() < 0.6 ? 0 : 2;
                    b.frame = rng.NextDouble() < 0.4 ? _frameCharcoal : rng.NextDouble() < 0.5 ? _frameBronze : rng.NextDouble() < 0.5 ? _frameWhite : _frameGreen;
                    break;
                default:
                    b.trim = _coping;
                    b.reveal = 0.22f;
                    b.surround = true;
                    b.joinery = rng.NextDouble() < 0.7 ? 2 : 1;
                    break;
            }
            if (b.joinery == 1 && rng.NextDouble() < 0.3) b.frame = _frameWood;
            b.muntins = b.joinery == 0 || (b.joinery == 1 && rng.NextDouble() < 0.5);
        }

        /// <summary>A rusticated stone ground floor, for the masonry styles (always under shops, often otherwise).</summary>
        private static void MaybeRusticBase(Mass b, bool shops)
        {
            if (b.oldStyle || b.style == FacadeStyle.Plaster || b.floors < 2) return;
            if (shops || b.rng.NextDouble() < 0.45) b.baseMat = b.rng.NextDouble() < 0.5 ? _rustic : _rusticDressed;
        }

        /// <summary>How far a window's head rises above its springing on this floor (0: flat).</summary>
        private static float WindowRise(Mass b, int floor, float width, bool groundInterior)
        {
            if (b.oldStyle || groundInterior) return 0f;              // the interior cuts the same opening, square
            if (b.roundTop && floor == b.floors - 1 && b.floors >= 2) return width * 0.5f;
            if (b.arches == 1) return width * 0.16f;
            return 0f;
        }

        // ------------------------------------------------------------------
        // Reveals
        // ------------------------------------------------------------------
        /// <summary>The four reveal faces of an opening through a wall <paramref name="depth"/> thick, the head following
        /// its arch. <paramref name="h"/> is (u0, u1, v0, v1) from <paramref name="wa"/> along <paramref name="u"/>.</summary>
        private static void Reveals(TownMesh m, Material mat, Vector3 wa, Vector3 u, Vector3 nrm, Vector4 h, float rise, float depth)
        {
            Vector3 inn = -nrm * depth;
            Vector3 P(float a, float v) => wa + u * a + Vector3.up * v;
            m.Quad(mat, P(h.x, h.z), P(h.y, h.z), P(h.y, h.z) + inn, P(h.x, h.z) + inn, Vector3.up);
            m.Quad(mat, P(h.x, h.z), P(h.x, h.w), P(h.x, h.w) + inn, P(h.x, h.z) + inn, u);
            m.Quad(mat, P(h.y, h.z), P(h.y, h.w), P(h.y, h.w) + inn, P(h.y, h.z) + inn, -u);
            if (rise <= 0f) { m.Quad(mat, P(h.x, h.w), P(h.y, h.w), P(h.y, h.w) + inn, P(h.x, h.w) + inn, Vector3.down); return; }
            float half = (h.y - h.x) * 0.5f, cx = (h.x + h.y) * 0.5f;
            const int N = 8;
            for (int i = 0; i < N; i++)
            {
                float a = h.x + (h.y - h.x) * i / N, b = h.x + (h.y - h.x) * (i + 1) / N;
                var pa = P(a, h.w + TownMesh.SegmentTop(a - cx, half, rise));
                var pb = P(b, h.w + TownMesh.SegmentTop(b - cx, half, rise));
                var down = Vector3.Cross(pb - pa, inn).normalized; if (down.y > 0f) down = -down;
                m.Quad(mat, pa, pb, pb + inn, pa + inn, down);
            }
        }

        // ------------------------------------------------------------------
        // Joinery and dressings
        // ------------------------------------------------------------------
        /// <summary>A bar with a front and four sides (no back: it stands on a surface): front face centred on
        /// <paramref name="c"/>, <paramref name="w"/> along <paramref name="u"/>, <paramref name="h"/> along
        /// <paramref name="v"/>, <paramref name="d"/> deep back along -<paramref name="n"/>.</summary>
        private static void Prism(TownMesh m, Material mat, Vector3 c, Vector3 u, Vector3 v, Vector3 n, float w, float h, float d)
        {
            Vector3 hu = u * (w * 0.5f), hv = v * (h * 0.5f), back = -n * d;
            Vector3 a = c - hu - hv, b = c + hu - hv, cc = c + hu + hv, dd = c - hu + hv;
            m.Quad(mat, a, b, cc, dd, n);
            m.Quad(mat, a, b, b + back, a + back, -v);
            m.Quad(mat, dd, cc, cc + back, dd + back, v);
            m.Quad(mat, a, dd, dd + back, a + back, -u);
            m.Quad(mat, b, cc, cc + back, b + back, u);
        }

        /// <summary>A prism between two points on a face (a frame member, an arch segment), <paramref name="t"/> across.</summary>
        /// Only its front and long sides: its ends butt into other members, and a window has dozens of bars. A thin
        /// member (glazing bars, sash rails, iron bars, 4.5 cm or less) is its front face alone.
        private static void Bar(TownMesh m, Material mat, Vector3 from, Vector3 to, Vector3 n, float t, float d)
        {
            var along = to - from; float len = along.magnitude;
            if (len < 0.005f) return;
            along /= len;
            var across = Vector3.Cross(n, along).normalized * (t * 0.5f);
            Vector3 back = -n * d;
            Vector3 a = from - across, b = to - across, c = to + across, e = from + across;
            m.Quad(mat, a, b, c, e, n);
            if (t <= 0.045f) return;
            m.Quad(mat, a, b, b + back, a + back, -across);
            m.Quad(mat, e, c, c + back, e + back, across);
        }

        /// <summary>
        /// A window in its opening: the glass (in the chunk), and in the detail mesh the frame, sashes and glazing bars, a
        /// fanlight in an arched head, the stone sill, the head's dressing (brick arch ring and keystone, stone surround,
        /// hood) and, for a French window, an iron balconette or balcony. <paramref name="p0"/>, <paramref name="p1"/> are
        /// the opening's bottom corners on the wall face, <paramref name="H"/> its height to the springing.
        /// </summary>
        private static void FacadeWindow(TownMesh shell, TownMesh det, Mass b, Vector3 p0, Vector3 p1, float H, float rise, float depth,
                                         Vector3 nrm, int floor, bool open, bool tall, bool rich = true)
        {
            var rng = b.rng;
            Vector3 u = (p1 - p0).normalized, up = Vector3.up;
            float W = Vector3.Distance(p0, p1);
            float openingH = H;                                        // H itself shrinks under a roller-shutter box
            float half = W * 0.5f;
            Vector3 inn = -nrm * depth;
            Vector3 glassAt = inn + nrm * 0.04f;                       // glass a few centimetres in front of the reveal's back
            Vector3 G(float x, float y) => p0 + u * x + up * y + glassAt;
            Vector3 F(float x, float y) => p0 + u * x + up * y;        // on the wall face
            float Arch(float x) => TownMesh.SegmentTop(x - half, half, rise);

            if (open)
            {
                // A ground-floor window of a house you can enter: open, barred.
                for (float s = 0.12f; s < W - 0.05f; s += 0.13f) Bar(det, _steel, G(s, 0f) + nrm * 0.1f, G(s, H) + nrm * 0.1f, nrm, 0.022f, 0.022f);
                Bar(det, _steel, G(0f, H * 0.33f) + nrm * 0.1f, G(W, H * 0.33f) + nrm * 0.1f, nrm, 0.03f, 0.02f);
                Bar(det, _steel, G(0f, H * 0.7f) + nrm * 0.1f, G(W, H * 0.7f) + nrm * 0.1f, nrm, 0.03f, 0.02f);
                Sill(det, b, p0, p1, depth, nrm);
                return;
            }

            // ---- glass: the rectangle, then the arch as a fan ----
            double pick = rng.NextDouble();
            Material glass = pick < 0.12 ? _glassCurtain : pick < 0.2 ? _glassBlinds : b.glass;
            shell.Quad(glass, G(0, 0), G(W, 0), G(W, H), G(0, H), nrm);
            if (rise > 0f)
            {
                const int N = 8;
                var hub = G(half, H);
                for (int i = 0; i < N; i++)
                {
                    float x0 = W * i / N, x1 = W * (i + 1) / N;
                    shell.Tri(glass, hub, G(x0, H + Arch(x0)), G(x1, H + Arch(x1)), nrm);
                }
            }

            // ---- a roller shutter on a modern block's window: its box under the head, the slats part way down ----
            if (b.style == FacadeStyle.Plaster && b.joinery == 2 && !tall && rng.NextDouble() < 0.85)
            {
                float box = 0.22f;
                Prism(det, _shutterBoxMat, F(half, H - box * 0.5f) - nrm * 0.01f, u, up, nrm, W, box, depth - 0.07f);
                double state = rng.NextDouble();
                float drop = state < 0.3 ? 0.04f : state < 0.75 ? Mathf.Lerp(0.25f, 0.6f, (float)rng.NextDouble()) : 1f;
                float bottomY = Mathf.Lerp(H - box, 0f, drop);
                if (H - box - bottomY > 0.02f)
                {
                    Vector3 sb = -nrm * 0.06f;
                    shell.Quad(_shutterPaint, F(0f, bottomY) + sb, F(W, bottomY) + sb, F(W, H - box) + sb, F(0f, H - box) + sb, nrm);
                    Prism(det, _shutterBoxMat, F(half, bottomY + 0.025f) - nrm * 0.045f, u, up, nrm, W - 0.02f, 0.05f, 0.03f);
                }
                foreach (float x in new[] { 0.02f, W - 0.02f })
                    Prism(det, _shutterBoxMat, F(x, (H - box) * 0.5f) - nrm * 0.05f, u, up, nrm, 0.035f, H - box, 0.05f);
                H -= box;                                                  // the window proper is below the box
            }

            // ---- frame: the outer frame round the opening, 6 cm, standing 6 cm proud of the glass ----
            var fm = b.frame;
            const float fw = 0.06f, fd = 0.07f;
            Vector3 fo = nrm * 0.035f;                                    // frame front, from the glass plane
            Bar(det, fm, G(fw * 0.5f, 0f) + fo, G(fw * 0.5f, H) + fo, nrm, fw, fd);
            Bar(det, fm, G(W - fw * 0.5f, 0f) + fo, G(W - fw * 0.5f, H) + fo, nrm, fw, fd);
            Bar(det, fm, G(0f, fw * 0.5f) + fo, G(W, fw * 0.5f) + fo, nrm, fw, fd);
            Bar(det, fm, G(0f, H - fw * 0.5f) + fo, G(W, H - fw * 0.5f) + fo, nrm, fw, fd);
            if (rise > 0f)
            {
                const int N = 6;
                for (int i = 0; i < N; i++)
                {
                    float x0 = W * i / N, x1 = W * (i + 1) / N;
                    Bar(det, fm, G(x0, H + Arch(x0) - fw * 0.5f) + fo, G(x1, H + Arch(x1) - fw * 0.5f) + fo, nrm, fw, fd);
                }
                // The fanlight's glazing: a bar up the middle, and two raking ones in a round head.
                if (rich) Bar(det, fm, G(half, H) + fo * 0.7f, G(half, H + rise - fw) + fo * 0.7f, nrm, 0.03f, 0.04f);
                if (rich && rise > half * 0.6f)
                {
                    Bar(det, fm, G(half, H) + fo * 0.7f, G(half - half * 0.7f, H + Arch(half - half * 0.7f) - fw) + fo * 0.7f, nrm, 0.025f, 0.04f);
                    Bar(det, fm, G(half, H) + fo * 0.7f, G(half + half * 0.7f, H + Arch(half + half * 0.7f) - fw) + fo * 0.7f, nrm, 0.025f, 0.04f);
                }
            }

            // ---- sashes and glazing bars (on faces over a street: the back and the yard get the frame alone) ----
            Vector3 so = nrm * 0.02f;
            switch (rich ? b.joinery : -1)
            {
                case 0:   // double-hung sash: the meeting rail a little above the middle, lights in each sash
                {
                    float meet = H * 0.52f;
                    Bar(det, fm, G(fw, meet) + so + nrm * 0.015f, G(W - fw, meet) + so + nrm * 0.015f, nrm, 0.055f, 0.06f);
                    Bar(det, fm, G(fw, meet + 0.03f) + so, G(W - fw, meet + 0.03f) + so, nrm, 0.045f, 0.04f);
                    if (b.muntins)
                    {
                        Bar(det, fm, G(half, fw) + so, G(half, H - fw) + so, nrm, 0.024f, 0.03f);
                        Bar(det, fm, G(fw, meet + (H - meet) * 0.5f) + so, G(W - fw, meet + (H - meet) * 0.5f) + so, nrm, 0.024f, 0.03f);
                        if (W > 1.3f)
                        {
                            Bar(det, fm, G(W / 3f, meet) + so, G(W / 3f, H - fw) + so, nrm, 0.024f, 0.03f);
                            Bar(det, fm, G(W * 2f / 3f, meet) + so, G(W * 2f / 3f, H - fw) + so, nrm, 0.024f, 0.03f);
                        }
                    }
                    break;
                }
                case 1:   // casements: a mullion, a transom with the fixed light above, sash frames in each leaf
                {
                    float tr = H * 0.72f;
                    Bar(det, fm, G(half, fw) + fo * 0.6f, G(half, H - fw) + fo * 0.6f, nrm, 0.06f, 0.06f);
                    Bar(det, fm, G(fw, tr) + fo * 0.6f, G(W - fw, tr) + fo * 0.6f, nrm, 0.055f, 0.06f);
                    foreach (var (x0, x1) in new[] { (fw, half - 0.03f), (half + 0.03f, W - fw) })
                    {
                        Bar(det, fm, G(x0 + 0.02f, fw) + so, G(x0 + 0.02f, tr - 0.03f) + so, nrm, 0.04f, 0.035f);
                        Bar(det, fm, G(x1 - 0.02f, fw) + so, G(x1 - 0.02f, tr - 0.03f) + so, nrm, 0.04f, 0.035f);
                        Bar(det, fm, G(x0, fw + 0.02f) + so, G(x1, fw + 0.02f) + so, nrm, 0.04f, 0.035f);
                        Bar(det, fm, G(x0, tr - 0.05f) + so, G(x1, tr - 0.05f) + so, nrm, 0.04f, 0.035f);
                        if (b.muntins) Bar(det, fm, G(x0, (fw + tr) * 0.5f) + so, G(x1, (fw + tr) * 0.5f) + so, nrm, 0.022f, 0.025f);
                    }
                    break;
                }
                case -1: break;
                default:  // modern sliding: two panes, the meeting stiles side by side
                {
                    Bar(det, fm, G(half - 0.025f, fw) + so, G(half - 0.025f, H - fw) + so, nrm, 0.05f, 0.04f);
                    Bar(det, fm, G(half + 0.025f, fw) + so + nrm * 0.03f, G(half + 0.025f, H - fw) + so + nrm * 0.03f, nrm, 0.05f, 0.04f);
                    break;
                }
            }

            // ---- sill and head (the head's dressing on faces over a street only) ----
            Sill(det, b, p0, p1, depth, nrm);
            switch (rich ? b.style : (FacadeStyle)(-1))
            {
                case FacadeStyle.Brick:
                    if (rise > 0f) ArchRing(det, b, F(0f, H), u, nrm, W, rise, 0.24f);
                    else
                    {
                        // A flat arch of brick on end, and a stone keystone.
                        Prism(det, _brickArch, F(half, H + 0.12f) + nrm * 0.015f, u, up, nrm, W + 0.24f, 0.24f, 0.015f);
                        Keystone(det, b, F(half, H + 0.02f), u, nrm, 0.3f);
                    }
                    break;
                case FacadeStyle.Ashlar:
                    Surround(det, b.trim, p0, u, nrm, W, H, rise, 0.13f, 0.035f);
                    if (rise > 0f) Keystone(det, b, F(half, H + rise - 0.04f), u, nrm, 0.34f);
                    else if (b.hood && floor == 1)
                    {
                        // A hood over the first floor's windows: a frieze and a projecting cornice.
                        Prism(det, b.trim, F(half, H + 0.22f) + nrm * 0.035f, u, up, nrm, W + 0.34f, 0.16f, 0.035f);
                        Prism(det, b.trim, F(half, H + 0.36f) + nrm * 0.14f, u, up, nrm, W + 0.52f, 0.1f, 0.14f);
                        Prism(det, b.trim, F(half, H + 0.43f) + nrm * 0.1f, u, up, nrm, W + 0.44f, 0.05f, 0.1f);
                    }
                    else Prism(det, b.trim, F(half, H + 0.2f) + nrm * 0.04f, u, up, nrm, W + 0.26f, 0.14f, 0.04f);
                    break;
                default:
                    if (b.surround && rich) Surround(det, b.trim, p0, u, nrm, W, openingH, rise, 0.09f, 0.018f);
                    break;
            }

            // ---- a French window: an iron balconette, or a full balcony with an iron railing ----
            if (tall)
            {
                if (b.style == FacadeStyle.Plaster) { Balcony(shell, b, p0, p1, nrm); return; }
                bool full = b.balconies && rng.NextDouble() < 0.5;
                IronBalcony(det, shell, b, p0, p1, nrm, full ? 1.1f : 0.36f);
            }
        }

        private static void Sill(TownMesh det, Mass b, Vector3 p0, Vector3 p1, float depth, Vector3 nrm)
        {
            Vector3 u = (p1 - p0).normalized;
            float W = Vector3.Distance(p0, p1);
            float extra = b.style == FacadeStyle.Ashlar ? 0.36f : 0.18f;
            float proud = b.style == FacadeStyle.Plaster ? 0.05f : 0.08f;
            var c = (p0 + p1) * 0.5f + nrm * proud + Vector3.up * -0.025f;
            Prism(det, b.trim, c, u, Vector3.up, nrm, W + extra, 0.075f, depth - 0.02f + proud);
        }

        /// <summary>A brick arch ring round a segmental or round head, a stone keystone at its crown and springers.</summary>
        private static void ArchRing(TownMesh det, Mass b, Vector3 spring0, Vector3 u, Vector3 nrm, float W, float rise, float ring)
        {
            float half = W * 0.5f;
            float radius = (half * half + rise * rise) / (2f * rise);
            Vector3 centre = spring0 + u * half + Vector3.up * (rise - radius);
            float a0 = Mathf.Atan2(radius - rise, -half), a1 = Mathf.Atan2(radius - rise, half);   // angles of the springing
            int n = Mathf.Clamp(Mathf.RoundToInt((a0 - a1) * radius / 0.2f), 6, 12);                 // the curve in 20 cm pieces
            for (int i = 0; i < n; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)n), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)n);
                Vector3 d0 = u * Mathf.Cos(t0) + Vector3.up * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + Vector3.up * Mathf.Sin(t1);
                Vector3 i0 = centre + d0 * radius, i1 = centre + d1 * radius, e0 = centre + d0 * (radius + ring), e1 = centre + d1 * (radius + ring);
                Vector3 f = nrm * 0.018f;
                det.Quad(_brickArch, i0 + f, i1 + f, e1 + f, e0 + f, nrm);
                det.Quad(_brickArch, e0, e1, e1 + f, e0 + f, (d0 + d1).normalized);
            }
            Keystone(det, b, centre + Vector3.up * (radius - 0.04f), u, nrm, ring + 0.1f);
            // Springer blocks where the arch starts.
            foreach (float s in new[] { 0f, W })
                Prism(det, b.trim, spring0 + u * s + Vector3.up * 0.06f + nrm * 0.03f, u, Vector3.up, nrm, 0.22f, 0.12f, 0.03f);
        }

        /// <summary>A tapered stone keystone, its foot at <paramref name="foot"/>, <paramref name="h"/> tall, proud of the arch.</summary>
        private static void Keystone(TownMesh det, Mass b, Vector3 foot, Vector3 u, Vector3 nrm, float h)
        {
            float wb = 0.15f, wt = 0.23f, d = 0.045f;
            Vector3 up = Vector3.up, f = nrm * d;
            Vector3 a = foot - u * (wb * 0.5f), bb = foot + u * (wb * 0.5f), c = foot + up * h + u * (wt * 0.5f), dd = foot + up * h - u * (wt * 0.5f);
            det.Quad(b.trim, a + f, bb + f, c + f, dd + f, nrm);
            det.Quad(b.trim, dd, c, c + f, dd + f, up);
            det.Quad(b.trim, a, bb, bb + f, a + f, -up);
            det.Quad(b.trim, a, dd, dd + f, a + f, Vector3.Cross(dd - a, nrm).normalized);
            det.Quad(b.trim, bb, c, c + f, bb + f, -Vector3.Cross(c - bb, nrm).normalized);
        }

        /// <summary>A stone architrave round an opening: jambs, and a head straight or following the arch.</summary>
        private static void Surround(TownMesh det, Material mat, Vector3 p0, Vector3 u, Vector3 nrm, float W, float H, float rise, float bw, float proud)
        {
            Vector3 up = Vector3.up;
            Vector3 F(float x, float y) => p0 + u * x + up * y + nrm * proud;
            Prism(det, mat, F(-bw * 0.5f, H * 0.5f), u, up, nrm, bw, H, proud);
            Prism(det, mat, F(W + bw * 0.5f, H * 0.5f), u, up, nrm, bw, H, proud);
            if (rise <= 0f) { Prism(det, mat, F(W * 0.5f, H + bw * 0.5f), u, up, nrm, W + bw * 2f, bw, proud); return; }
            float half = W * 0.5f;
            const int N = 7;
            for (int i = 0; i < N; i++)
            {
                float x0 = -bw + (W + bw * 2f) * i / N, x1 = -bw + (W + bw * 2f) * (i + 1) / N;
                float y0 = H + TownMesh.SegmentTop(Mathf.Clamp(x0, 0f, W) - half, half, rise) + bw * 0.5f;
                float y1 = H + TownMesh.SegmentTop(Mathf.Clamp(x1, 0f, W) - half, half, rise) + bw * 0.5f;
                Bar(det, mat, F(x0, y0), F(x1, y1), nrm, bw, proud);
            }
        }

        /// <summary>An iron railing on a stone slab under a French window: <paramref name="deep"/> 0.36 m for a
        /// balconette, about a metre for a balcony.</summary>
        private static void IronBalcony(TownMesh det, TownMesh shell, Mass b, Vector3 p0, Vector3 p1, Vector3 nrm, float deep)
        {
            Vector3 u = (p1 - p0).normalized, up = Vector3.up;
            float W = Vector3.Distance(p0, p1) + (deep > 0.5f ? 0.9f : 0.3f);
            Vector3 c = (p0 + p1) * 0.5f + nrm * (deep * 0.5f);
            // The slab (in the chunk: it is big enough to be seen from far), its moulded edge, corbels under it.
            shell.Box(b.trim, c - up * 0.07f, new Vector3(W, 0.14f, deep), Quaternion.LookRotation(nrm));
            for (float s = -W * 0.5f + 0.18f; s <= W * 0.5f - 0.17f; s += Mathf.Max(0.5f, (W - 0.36f) / Mathf.Max(1, Mathf.Round((W - 0.36f) / 0.7f))))
                Prism(det, b.trim, c - nrm * (deep * 0.5f) + u * s - up * 0.25f + nrm * (deep * 0.4f), u, up, nrm, 0.14f, 0.22f, deep * 0.4f);
            // The railing: top and bottom rails round the three sides, bars at 11 cm with a scroll at the top of each.
            Vector3 l0 = c - u * (W * 0.5f - 0.04f) - nrm * (deep * 0.5f), r0 = c + u * (W * 0.5f - 0.04f) - nrm * (deep * 0.5f);
            Vector3 l1 = l0 + nrm * (deep - 0.04f), r1 = r0 + nrm * (deep - 0.04f);
            foreach (float y in new[] { 0.08f, 0.95f })
            {
                Bar(det, _iron, l1 + up * y, r1 + up * y, up, 0.035f, 0.03f);
                Bar(det, _iron, l0 + up * y, l1 + up * y, up, 0.035f, 0.03f);
                Bar(det, _iron, r0 + up * y, r1 + up * y, up, 0.035f, 0.03f);
            }
            for (float s = 0.06f; s < W - 0.08f; s += 0.12f)
            {
                var foot = l1 + u * s;
                Bar(det, _iron, foot + up * 0.08f, foot + up * 0.95f, nrm, 0.016f, 0.016f);
                Bar(det, _iron, foot + up * 0.86f, foot + up * 0.95f + u * 0.05f, nrm, 0.012f, 0.012f);
            }
            for (float s = 0.11f; s < deep - 0.06f; s += 0.11f)
                foreach (var side in new[] { l0, r0 })
                    Bar(det, _iron, side + nrm * s + up * 0.08f, side + nrm * s + up * 0.95f, u, 0.016f, 0.016f);
        }

        // ------------------------------------------------------------------
        // The whole face: base course, string courses, cornice, quoins
        // ------------------------------------------------------------------
        /// <summary>The dressings that run along a face (not a blank party wall): the plinth course over a rusticated
        /// base, a string course at each floor, the cornice under the parapet with dentils and brackets, and quoins.</summary>
        private static void FaceDressings(TownMesh shell, TownMesh det, Mass b, Vector3 a0, Vector3 b0, Vector3 nrm, int side,
                                          bool quoinStart = true, bool quoinEnd = true, bool rich = true)
        {
            float len = Vector3.Distance(a0, b0);
            Vector3 u = (b0 - a0) / Mathf.Max(0.01f, len), up = Vector3.up;
            Vector3 mid = (a0 + b0) * 0.5f;
            var rot = Quaternion.LookRotation(nrm);
            Material t = b.trim;

            if (b.style == FacadeStyle.Plaster)
            {
                // Plaster blocks: a slab band at each floor and a deeper one at the roof.
                for (int f = 1; f <= b.floors; f++)
                {
                    float y = f == b.floors ? b.Top : b.FloorY(f);
                    float hh = f == b.floors ? 0.3f : 0.2f;
                    shell.Box(t, mid + up * (y - hh * 0.5f) + nrm * 0.035f, new Vector3(len + 0.07f, hh, 0.07f), rot);
                }
                return;
            }

            // Plinth course on top of a rusticated ground floor; string courses at the floors above.
            for (int f = 1; f < b.floors; f++)
            {
                float y = b.FloorY(f);
                bool course = f == 1 && b.baseMat != null;
                float hh = course ? 0.24f : 0.15f, pr = course ? 0.08f : 0.05f;
                shell.Box(t, mid + up * (y - 0.02f) + nrm * (pr * 0.5f), new Vector3(len + pr * 2f, hh, pr), rot);
            }

            // The cornice: frieze, bed moulding, corona, crowning moulding -- then dentils and brackets under it.
            float top = b.Top;
            shell.Box(t, mid + up * (top - 0.18f) + nrm * 0.012f, new Vector3(len + 0.024f, 0.36f, 0.024f), rot);
            shell.Box(t, mid + up * (top + 0.05f) + nrm * 0.06f, new Vector3(len + 0.12f, 0.1f, 0.12f), rot);
            shell.Box(t, mid + up * (top + 0.18f) + nrm * 0.2f, new Vector3(len + 0.4f, 0.16f, 0.4f), rot);
            shell.Box(t, mid + up * (top + 0.29f) + nrm * 0.22f, new Vector3(len + 0.44f, 0.06f, 0.44f), rot);
            // (Dentils: front and underside only; brackets: one block, no back. Repeated round every masonry face these
            // were most of the detail mesh's triangles; on a face that is not over a street there are none, nor quoins.)
            if (!rich) return;
            for (float s = 0.12f; s < len - 0.06f; s += 0.3f)
            {
                Vector3 c = a0 + u * s + up * (top - 0.02f) + nrm * 0.13f;
                Vector3 hu = u * 0.045f, hv = up * 0.05f, back = -nrm * 0.1f;
                det.Quad(t, c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv, nrm);
                det.Quad(t, c - hu - hv, c + hu - hv, c + hu - hv + back, c - hu - hv + back, -up);
            }
            int brackets = Mathf.Max(2, Mathf.RoundToInt(len / 0.95f));
            for (int i = 0; i <= brackets; i++)
            {
                var at = a0 + u * Mathf.Lerp(0.2f, len - 0.2f, i / (float)brackets);
                Vector3 c = at + up * (top + 0.02f) + nrm * 0.36f;
                Vector3 hu = u * 0.06f, hv = up * 0.08f, back = -nrm * 0.34f;
                det.Quad(t, c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv, nrm);
                det.Quad(t, c - hu - hv, c + hu - hv, c + hu - hv + back, c - hu - hv + back, -up);
                det.Quad(t, c - hu - hv, c - hu + hv, c - hu + hv + back, c - hu - hv + back, -u);
                det.Quad(t, c + hu - hv, c + hu + hv, c + hu + hv + back, c + hu - hv + back, u);
            }

            // Quoins at both ends of the face: long and short blocks in turn, the faces at a corner out of step so they lock.
            float q0 = b.baseMat != null ? b.FloorY(1) + 0.1f : b.baseY + 0.1f;
            int course0 = side % 2;
            foreach (bool atStart in new[] { true, false })
            {
                if (atStart ? !quoinStart : !quoinEnd) continue;
                Vector3 edge = atStart ? a0 : b0;
                Vector3 inward = atStart ? u : -u;
                int k = course0;
                for (float y = q0; y + 0.3f < top - 0.38f; y += 0.33f, k++)
                {
                    // Each block a face 2.5 cm proud of the wall (its sides are a sliver; a full block each was 75 000 of them).
                    float wq = k % 2 == 0 ? 0.56f : 0.34f;
                    Vector3 c = edge + inward * (wq * 0.5f) + up * (y + 0.15f) + nrm * 0.025f;
                    Vector3 hu = u * (wq * 0.5f), hv = up * 0.15f;
                    det.Quad(t, c - hu - hv, c + hu - hv, c + hu + hv, c - hu + hv, nrm);
                    det.Quad(t, c - hu + hv, c + hu + hv, c + hu + hv - nrm * 0.025f, c - hu + hv - nrm * 0.025f, up);
                }
            }
        }

        /// <summary>A stone architrave and a hood round a front door of the masonry styles, a fanlight-like transom over it.</summary>
        private static void DoorDressing(TownMesh det, Mass b, Vector3 p0, Vector3 p1, float H, Vector3 nrm)
        {
            if (b.style == FacadeStyle.Plaster) return;
            Vector3 u = (p1 - p0).normalized;
            float W = Vector3.Distance(p0, p1);
            Surround(det, b.trim, p0, u, nrm, W, H, 0f, 0.18f, 0.05f);
            var c = (p0 + p1) * 0.5f + Vector3.up * (H + 0.32f);
            Prism(det, b.trim, c + nrm * 0.2f, u, Vector3.up, nrm, W + 0.8f, 0.1f, 0.2f);
            Prism(det, b.trim, c - Vector3.up * 0.09f + nrm * 0.08f, u, Vector3.up, nrm, W + 0.6f, 0.1f, 0.08f);
        }
    }
}
#endif
