#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's famous places -- the ones later levels will be built round (enemies holding the mosque, the
    /// citadel, the palace, the souk). Each is its own named object under Arena/Landmarks so a level or a test can
    /// find it, and each can be walked into: the mosque's courtyard, arcades and prayer hall and the minaret's
    /// stair to its balcony; the citadel's gate, courtyard, wall-walk and keep; the palace garden and hall; the souk's
    /// covered street from the bazaar gate on the clock-tower roundabout.
    ///
    /// Stairs that have to be climbed by enemies too are ramps under 26 degrees with flat landings (the navigation
    /// bake takes those cleanly); narrow tops, merlons and domes are kept off the bake.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _stone, _stoneWarm, _stucco, _domeTile, _gold, _castle, _rubbleMat, _carpet, _townWater, _marble, _clockFace;

        private static void ResolveLandmarkMaterials()
        {
            _stone = TownMat("Limestone", "white_sandstone_bricks", new Color(1.02f, 0.97f, 0.90f), 3.2f);
            _stoneWarm = TownMat("StoneWarm", "white_sandstone_bricks", new Color(1.0f, 0.88f, 0.72f), 3.2f);
            _stucco = TownMat("StuccoWhite", "white_stucco", new Color(1.0f, 0.98f, 0.95f), 3f);
            _domeTile = TownMat("DomeTile", "marble_mosaic_tiles", new Color(0.42f, 0.78f, 0.80f), 1.6f, 1.2f);
            _gold = TownFlat("Gold", new Color(0.85f, 0.66f, 0.28f), 0.75f, 1f);
            _castle = TownMat("CastleStone", "castle_wall_varriation", new Color(1.05f, 0.88f, 0.70f), 4f);
            _rubbleMat = TownMat("Rubble", "rubble", new Color(1f, 0.92f, 0.82f), 3f);
            _marble = TownMat("MosqueFloor", "old_mosaic_floor", new Color(1f, 0.97f, 0.92f), 3f);
            _carpet = CarpetMat();
            _townWater = TownWater("TownWater", new Color(0.22f, 0.36f, 0.34f), new Color(0.03f, 0.16f, 0.16f));
            _clockFace = UnlitAtlas("TownClock", "TownClock.png", 1.0f);
        }

        private static Material CarpetMat()
        {
            string tex = $"{FPSKitTownAssets.Out}/Textures/TownCarpets.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            string path = $"{FPSKitTownAssets.MatFolder}/Town_Carpets.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.05f);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Water: an opaque Lit material on the generated water normal map, as the old desert's river was. The water shader
        /// the user imported needs the camera's opaque and depth textures, which the phone tier and the capture camera do
        /// not have (it came out invisible), so it is not used.
        /// </summary>
        private static Material TownWater(string name, Color shallow, Color deep)
        {
            _ = deep;
            return MakeDetailMaterial(name, shallow, "Water", 0.22f, 0.86f, 0.05f, 1.0f);
        }

        private static Material TownWaterShader(string name, Color shallow, Color deep)
        {
            string path = $"{FPSKitTownAssets.MatFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("FX/ProceduralWater");
            if (shader == null) return TownFlat(name + "Flat", shallow, 0.95f, 0f, transparent: true);
            bool create = m == null;
            if (create) m = new Material(shader);
            m.shader = shader;
            m.SetColor("_BaseColor", shallow);
            m.SetColor("_DeepColor", deep);
            m.SetFloat("_RippleScale", 3.5f);
            m.SetFloat("_RippleSpeed", 0.5f);
            m.SetFloat("_RippleStrength", 0.35f);
            m.SetFloat("_FoamIntensity", 0.25f);
            m.SetFloat("_DepthMax", 3f);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        private static void BuildTownLandmarks(Transform root, int layer, System.Random rng)
        {
            ResolveLandmarkMaterials();
            var group = new GameObject("Landmarks").transform;
            group.SetParent(root, false);
            var props = new GameObject("LandmarkProps").transform;
            props.SetParent(group, false);

            BuildGrandMosque(group, layer, props, rng);
            BuildCitadel(group, layer, props, rng);
            BuildPalace(group, layer, props, rng);
            BuildClockTower(group, layer, props);
            BuildBazaarGate(group, layer);
            BuildSouk(group, layer, props, rng);
            BuildNeighbourhoodMosque(group, layer, rng);
        }

        // ==================================================================
        // Shared pieces
        // ==================================================================
        /// <summary>A solid wall between two ground points, <paramref name="t"/> thick, from <paramref name="y0"/> to <paramref name="y1"/>.</summary>
        private static void SolidWall(TownMesh m, Material mat, Vector2 a, Vector2 b, float y0, float y1, float t, bool top = true,
                                      float noEntryTop = float.NaN)
        {
            var d = (b - a); float len = d.magnitude; d /= len;
            var c = (a + b) * 0.5f;
            m.Box(mat, new Vector3(c.x, (y0 + y1) * 0.5f, c.y), new Vector3(len, y1 - y0, t), Quaternion.LookRotation(new Vector3(-d.y, 0f, d.x)) , bottom: false, top: top);
            // A solid wall stands on terrain that runs on inside it: that ground bakes as a strip of navmesh walled in on
            // every side. Kept off the bake below its top (or entirely, when the top is not a place to stand).
            if (!float.IsNaN(noEntryTop) && (Mathf.Abs(d.x) < 0.01f || Mathf.Abs(d.y) < 0.01f))
            {
                var n = new Vector2(-d.y, d.x) * (t * 0.5f);
                var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x, a.x + n.x, a.x - n.x), Mathf.Min(a.y, b.y, a.y + n.y, a.y - n.y),
                                        Mathf.Max(a.x, b.x, b.x + n.x, b.x - n.x), Mathf.Max(a.y, b.y, b.y + n.y, b.y - n.y));
                NoEntryBox(r, y0 - 1f, noEntryTop, 0.05f);
            }
        }

        /// <summary>Merlons along a wall top, on its outer edge.</summary>
        private static void Merlons(TownMesh m, Material mat, Vector2 a, Vector2 b, float y, float t, Vector2 outward, float w = 0.9f, float h = 1.1f)
        {
            var d = b - a; float len = d.magnitude; d /= len;
            var rot = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));
            for (float s = w * 0.5f; s < len; s += w * 2f)
            {
                var p = a + d * s + outward * (t * 0.5f - 0.25f);
                m.Box(mat, new Vector3(p.x, y + h * 0.5f, p.y), new Vector3(w, h, 0.5f), rot);
            }
        }

        /// <summary>A dome by revolution: a hemisphere stretched to <paramref name="height"/>; onion bulges and points.</summary>
        private static void Dome(TownMesh m, Material mat, Vector3 baseCentre, float radius, float height, bool onion, int seg = 32)
        {
            var prof = new List<Vector2>();
            const int rows = 14;
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                float a = t * Mathf.PI * 0.5f;
                float r = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * height;
                if (onion) { r *= 1f + 0.18f * Mathf.Sin(t * Mathf.PI) - 0.05f * t; if (i == rows) r = 0f; y = Mathf.Pow(t, 0.85f) * height; }
                prof.Add(new Vector2(Mathf.Max(0f, r), y));
            }
            m.Revolve(mat, baseCentre, prof, seg);
        }

        /// <summary>A crescent finial: a gold rod, two balls and a crescent ring.</summary>
        private static void Finial(TownMesh m, Vector3 at, float scale)
        {
            m.Cylinder(_gold, at, 0.06f * scale, 1.6f * scale, 8);
            Dome(m, _gold, at + Vector3.up * 0.5f * scale, 0.22f * scale, 0.22f * scale, false, 12);
            Dome(m, _gold, at + Vector3.up * 0.9f * scale, 0.16f * scale, 0.16f * scale, false, 12);
            // Crescent: an arc of small boxes.
            var c = at + Vector3.up * 1.75f * scale;
            for (int i = 0; i < 10; i++)
            {
                float a0 = Mathf.Lerp(-130f, 130f, i / 9f) * Mathf.Deg2Rad;
                var p = c + new Vector3(Mathf.Sin(a0) * 0.32f * scale, Mathf.Cos(a0) * 0.32f * scale, 0f);
                m.Box(_gold, p, new Vector3(0.1f, 0.1f, 0.06f) * scale, Quaternion.Euler(0f, 0f, -a0 * Mathf.Rad2Deg));
            }
        }

        /// <summary>
        /// A ramp from <paramref name="a"/> to <paramref name="b"/> (centres of its ends, with their heights), solid down
        /// to <paramref name="floor"/>, with low walls on both sides. Under 26 degrees so the bake walks it.
        /// </summary>
        private static void Ramp(TownMesh m, Material mat, Vector3 a, Vector3 b, float width, float floor, bool walls = true)
        {
            var d = b - a; d.y = 0f; float len = d.magnitude; d /= len;
            var side = Vector3.Cross(Vector3.up, d) * (width * 0.5f);
            Vector3 a0 = a - side, a1 = a + side, b0 = b - side, b1 = b + side;
            var up = Vector3.Cross(b0 - a0, a1 - a0).normalized; if (up.y < 0) up = -up;
            m.Quad(mat, a0, a1, b1, b0, up);
            Vector3 F(Vector3 p) => new Vector3(p.x, floor, p.z);
            m.Quad(mat, F(a0), F(b0), b0, a0, -side);
            m.Quad(mat, F(a1), F(b1), b1, a1, side);
            m.Quad(mat, F(b0), F(b1), b1, b0, d);
            if (walls)
            {
                m.Quad(mat, a0 + Vector3.up * 0.9f, b0 + Vector3.up * 0.9f, b0, a0, side);
                m.Quad(mat, a1 + Vector3.up * 0.9f, b1 + Vector3.up * 0.9f, b1, a1, -side);
                m.Quad(mat, a0 + Vector3.up * 0.9f - side * 0.1f, b0 + Vector3.up * 0.9f - side * 0.1f, b0 + Vector3.up * 0.9f, a0 + Vector3.up * 0.9f, Vector3.up);
            }
        }

        // ==================================================================
        // The grand mosque
        // ==================================================================
        /// <summary>
        /// A congregational mosque at the end of the avenue: a monumental portal from the plaza into an arcaded
        /// courtyard with an ablution fountain, the prayer hall on its north side under a tiled central dome and four
        /// small ones, carpets and columns inside, and a 46 m minaret at the courtyard's south-east corner with a ramp
        /// up inside it to the lower balcony -- the best view, and the most dangerous perch, in town.
        /// </summary>
        private static void BuildGrandMosque(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var r = MosqueRect;
            float y0 = SampleTownBase(r.center.x, r.yMin + 10f) + 0.25f;
            var (low, _) = GroundRange(r);
            float bottom = low - 0.6f;

            // The plaza in front: stone paving from the avenue's end to the portal, with benches and lamps (street pass).
            var plaza = MosquePlaza;
            m.Quad(_paversPlaza, new Vector3(plaza.xMin, y0 - 0.02f, plaza.yMin), new Vector3(plaza.xMax, y0 - 0.02f, plaza.yMin),
                   new Vector3(plaza.xMax, y0 - 0.02f, plaza.yMax), new Vector3(plaza.xMin, y0 - 0.02f, plaza.yMax), Vector3.up);
            m.BoxMinMax(_kerb, new Vector3(plaza.xMin, bottom, plaza.yMin - 0.3f), new Vector3(plaza.xMax, y0 - 0.02f, plaza.yMin), bottom: false);

            // Footprint: courtyard z 192..248, prayer hall z 248..296, x -50..50. Outer walls 1 m thick, 11 m tall.
            float x0 = -50f, x1 = 50f, zc0 = 190f, zc1 = 246f, zh1 = 294f;
            const float WallH = 11f, T = 1f;
            // Courtyard floor (marble) and the hall floor.
            m.Quad(_marble, new Vector3(x0, y0, zc0), new Vector3(x1, y0, zc0), new Vector3(x1, y0, zh1), new Vector3(x0, y0, zh1), Vector3.up);

            // Outer walls. South wall has the portal (8 m wide x 9 m), west and east walls have doors to the side streets.
            void OuterWall(Vector3 a, Vector3 b, Vector3 n, List<Vector4> holes)
            {
                m.WallWithHoles(_stone, a + Vector3.up * bottom, b + Vector3.up * bottom, WallH + y0 - bottom, n, holes);
                m.WallWithHoles(_stone, a - n * T + Vector3.up * bottom, b - n * T + Vector3.up * bottom, WallH + y0 - bottom, -n, holes);
                float len = Vector3.Distance(a, b); var u = (b - a) / len;
                m.Box(_coping, (a + b) * 0.5f - n * T * 0.5f + Vector3.up * (y0 + WallH + 0.05f), new Vector3(len + 0.2f, 0.1f, T + 0.2f), Quaternion.LookRotation(n));
                foreach (var h in holes)
                {
                    foreach (float e in new[] { h.x, h.y })
                        m.Quad(_stone, a + u * e + Vector3.up * (bottom + h.z), a + u * e - n * T + Vector3.up * (bottom + h.z),
                               a + u * e - n * T + Vector3.up * (bottom + h.w), a + u * e + Vector3.up * (bottom + h.w), e == h.x ? u : -u);
                    m.Quad(_stone, a + u * h.x + Vector3.up * (bottom + h.w), a + u * h.y + Vector3.up * (bottom + h.w),
                           a + u * h.y - n * T + Vector3.up * (bottom + h.w), a + u * h.x - n * T + Vector3.up * (bottom + h.w), Vector3.down);
                }
                // Crenellated cresting along the top, the way the old mosques finish a wall.
                for (float s = 0.6f; s < len - 0.4f; s += 1.6f)
                    m.Box(_stone, a + u * s - n * T * 0.5f + Vector3.up * (y0 + WallH + 0.55f), new Vector3(0.7f, 0.9f, 0.5f), Quaternion.LookRotation(n));
            }
            float pv = y0 - bottom;
            OuterWall(new Vector3(x0, 0, zc0), new Vector3(x1, 0, zc0), Vector3.back, new List<Vector4> { new Vector4(46f, 54f, pv, pv + 8.5f) });
            OuterWall(new Vector3(x1, 0, zh1), new Vector3(x0, 0, zh1), Vector3.forward, new List<Vector4>());
            OuterWall(new Vector3(x1, 0, zc0), new Vector3(x1, 0, zh1), Vector3.right, new List<Vector4> { new Vector4(26f, 28.6f, pv, pv + 3.2f), new Vector4(80f, 82.6f, pv, pv + 3.2f) });
            OuterWall(new Vector3(x0, 0, zh1), new Vector3(x0, 0, zc0), Vector3.left, new List<Vector4> { new Vector4(22f, 24.6f, pv, pv + 3.2f), new Vector4(76f, 78.6f, pv, pv + 3.2f) });

            // The portal: a tall frame proud of the south wall around the entrance, a pointed arch, the inscription band.
            var pc = new Vector3(0f, 0f, zc0);
            m.BoxMinMax(_stone, new Vector3(-8f, bottom, zc0 - 2.2f), new Vector3(-4f, y0 + 15f, zc0));
            m.BoxMinMax(_stone, new Vector3(4f, bottom, zc0 - 2.2f), new Vector3(8f, y0 + 15f, zc0));
            m.BoxMinMax(_stone, new Vector3(-8f, y0 + 11.5f, zc0 - 2.2f), new Vector3(8f, y0 + 15f, zc0));
            var archHoles = new List<Vector3> { new Vector3(4f, 8f, 6.5f) };
            m.WallWithArches(_stucco, new Vector3(-4f, y0, zc0 - 2.2f), new Vector3(4f, y0, zc0 - 2.2f), 11.5f, Vector3.back, archHoles, true);
            m.ArchSoffit(_stucco, new Vector3(-4f, y0, zc0 - 2.2f), Vector3.right, Vector3.forward, 4f, 8f, 6.5f, 2.2f, true);
            // Corner finials on the portal.
            m.Cylinder(_stone, new Vector3(-7.4f, y0 + 15f, zc0 - 1.1f), 0.5f, 1.6f, 10);
            m.Cylinder(_stone, new Vector3(7.4f, y0 + 15f, zc0 - 1.1f), 0.5f, 1.6f, 10);
            Dome(m, _domeTile, new Vector3(-7.4f, y0 + 16.6f, zc0 - 1.1f), 0.6f, 0.9f, true, 12);
            Dome(m, _domeTile, new Vector3(7.4f, y0 + 16.6f, zc0 - 1.1f), 0.6f, 0.9f, true, 12);

            // Courtyard arcades (riwaq) on east, west and south, 6 m deep, roofed.
            void Arcade(Vector3 a, Vector3 b, Vector3 n, float depth)
            {
                float len = Vector3.Distance(a, b); var u = (b - a) / len;
                int spans = Mathf.Max(1, Mathf.RoundToInt(len / 4.4f));
                float sw = len / spans;
                var arches = new List<Vector3>();
                for (int i = 0; i < spans; i++) arches.Add(new Vector3(sw * (i + 0.5f), sw - 1.0f, 3.6f));
                m.WallWithArches(_stucco, a + Vector3.up * y0, b + Vector3.up * y0, 6.2f, n, arches, true);
                m.WallWithArches(_stucco, a - n * 0.8f + Vector3.up * y0, b - n * 0.8f + Vector3.up * y0, 6.2f, -n, arches, true);
                foreach (var ar in arches) m.ArchSoffit(_stucco, a + Vector3.up * y0, u, -n, ar.x, ar.y, ar.z, 0.8f, true);
                // Roof slab over the arcade and its parapet.
                m.BoxMinMax(_stucco, Vector3.Min(a, b - n * depth) + Vector3.up * (y0 + 6.2f), Vector3.Max(a, b - n * depth) + Vector3.up * (y0 + 6.6f));
                // Ceiling under it, the walk under it is marble (the courtyard floor runs under).
            }
            Arcade(new Vector3(x0 + 7f, 0, zc0 + 7f), new Vector3(x1 - 7f, 0, zc0 + 7f), Vector3.forward, 6f);         // south (faces courtyard north)
            Arcade(new Vector3(x1 - 7f, 0, zc0 + 7f), new Vector3(x1 - 7f, 0, zc1), Vector3.left, 6f);                 // east
            Arcade(new Vector3(x0 + 7f, 0, zc1), new Vector3(x0 + 7f, 0, zc0 + 7f), Vector3.right, 6f);                // west

            // Ablution fountain: octagonal basin with water, a little domed canopy on columns.
            var fc = new Vector3(0f, y0, (zc0 + zc1) * 0.5f + 3f);
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var p0 = fc + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 3.2f; var p1 = fc + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 3.2f;
                SolidWall(m, _marble, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), y0, y0 + 0.6f, 0.3f);
                var c0 = fc + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 2.6f;
                m.Cylinder(_stucco, c0, 0.18f, 3.6f, 8);
            }
            m.Disc(_townWater, fc + Vector3.up * 0.45f, 3.05f, 24, Vector3.up);
            m.Cylinder(_stucco, fc + Vector3.up * 3.6f, 3.0f, 0.4f, 16, true, true);
            Dome(m, _domeTile, fc + Vector3.up * 4.0f, 2.6f, 2.2f, false, 20);

            // The prayer hall: its courtyard face is an arcade of seven open arches (the way in), roof at 10 m.
            {
                var a = new Vector3(x0 + T, 0, zc1); var b = new Vector3(x1 - T, 0, zc1);
                float len = b.x - a.x; var arches = new List<Vector3>();
                for (int i = 0; i < 7; i++) arches.Add(new Vector3(len * (i + 0.5f) / 7f, 5.2f, 4.6f));
                m.WallWithArches(_stone, a + Vector3.up * y0, b + Vector3.up * y0, 10.5f, Vector3.back, arches, true);
                m.WallWithArches(_stone, a + Vector3.forward * 1f + Vector3.up * y0, b + Vector3.forward * 1f + Vector3.up * y0, 10.5f, Vector3.forward, arches, true);
                foreach (var ar in arches) m.ArchSoffit(_stone, a + Vector3.up * y0, Vector3.right, Vector3.forward, ar.x, ar.y, ar.z, 1f, true);
                // Roof and ceiling.
                m.BoxMinMax(_stucco, new Vector3(x0, y0 + 10.5f, zc1), new Vector3(x1, y0 + 11f, zh1), bottom: true);
                // Carpets over the hall floor: the prayer-row carpet, tiled.
                float cy = y0 + 0.02f;
                var cell = new Rect(0f, 0f, 0.5f, 0.5f);
                for (float x = x0 + T + 0.5f; x < x1 - T - 0.5f; x += 6f)
                    for (float z = zc1 + 1.5f; z < zh1 - T - 2f; z += 6f)
                    {
                        float xe = Mathf.Min(x + 6f, x1 - T - 0.5f), ze = Mathf.Min(z + 6f, zh1 - T - 2f);
                        m.QuadUV(_carpet, new Vector3(x, cy, z), new Vector3(xe, cy, z), new Vector3(xe, cy, ze), new Vector3(x, cy, ze), Vector3.up,
                                 new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
                    }
                // Columns in rows carrying the roof.
                for (float x = x0 + 8f; x < x1 - 4f; x += 8f)
                    for (float z = zc1 + 8f; z < zh1 - 4f; z += 8f)
                        if (Mathf.Abs(x) > 11f || z < zc1 + 14f || z > zh1 - 10f)
                        {
                            m.Cylinder(_stucco, new Vector3(x, y0, z), 0.42f, 10.5f, 12, false);
                            m.BoxMinMax(_stone, new Vector3(x - 0.55f, y0, z - 0.55f), new Vector3(x + 0.55f, y0 + 0.5f, z + 0.55f));
                        }
                // Mihrab: a niche in the qibla wall (north), framed.
                m.BoxMinMax(_stone, new Vector3(-2.2f, y0, zh1 - T - 0.6f), new Vector3(2.2f, y0 + 6f, zh1 - T));
                m.WallWithArches(_domeTile, new Vector3(-1.5f, y0, zh1 - T - 0.61f), new Vector3(1.5f, y0, zh1 - T - 0.61f), 5.5f, Vector3.back, new List<Vector3> { new Vector3(1.5f, 2.4f, 3f) }, true);
                // Minbar: the stepped pulpit beside it.
                m.Stairs(_doorWood, new Vector3(4.5f, y0, zh1 - T - 6f), Vector3.forward, 1.0f, 2.6f, 0.26f, 0.42f);
            }

            // The central dome on its drum, and four small domes.
            var dc = new Vector3(0f, y0 + 11f, (zc1 + zh1) * 0.5f);
            m.Cylinder(_stone, dc, 11.5f, 4f, 40, false);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                var wp = dc + new Vector3(Mathf.Cos(a) * 11.55f, 2f, Mathf.Sin(a) * 11.55f);
                m.Box(_glassDark, wp, new Vector3(0.9f, 2.0f, 0.05f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))));
            }
            m.Cylinder(_coping, dc + Vector3.up * 4f, 11.9f, 0.4f, 40, false);
            Dome(m, _domeTile, dc + Vector3.up * 4.4f, 11.4f, 10.5f, false, 48);
            Finial(m, dc + Vector3.up * 14.9f, 2.2f);
            foreach (var o in new[] { new Vector3(-26f, 0, -10f), new Vector3(26f, 0, -10f), new Vector3(-26f, 0, 12f), new Vector3(26f, 0, 12f) })
            {
                var c = new Vector3(o.x, y0 + 11f, dc.z + o.z);
                m.Cylinder(_stone, c, 4.2f, 1.6f, 24, false);
                Dome(m, _stucco, c + Vector3.up * 1.6f, 4.1f, 3.8f, false, 28);
                Finial(m, c + Vector3.up * 5.4f, 0.9f);
            }

            var go = m.Build(group, "GrandMosque", layer, "Concrete");

            BuildMinaret(group, layer, new Vector3(x1 - 3.2f, y0, zc0 + 3.2f), y0, bottom);

            // Lanterns hung in the arcades and hall.
            for (float x = x0 + 12f; x < x1 - 8f; x += 12f)
                PlaceTown(props, Prop("brass_diya_lantern"), new Vector3(x, y0 + 7.4f, (zc1 + zh1) * 0.5f + (x % 24 == 0 ? 6f : -6f)), 0f, 0.6f, navBlock: false);
            // Shoe racks and a few rugs by the doors.
            PlaceTown(props, Prop("wooden_display_shelves_01"), new Vector3(-12f, y0, zc1 - 1.5f), 180f, 1.6f);
            PlaceTown(props, Prop("wooden_display_shelves_01"), new Vector3(12f, y0, zc1 - 1.5f), 180f, 1.6f);

            // Off the bake: the roofs (no way up) -- volumes over the hall roof, arcade roofs and the dome drum.
            NoEntryBox(Rect.MinMaxRect(x0, zc1, x1, zh1), y0 + 10.2f, y0 + 40f, 0f);
            // The minaret's corner (x1-7..x1, zc0..zc0+7) is left out: its ramps climb through that height.
            NoEntryBox(Rect.MinMaxRect(x0, zc0, x1 - 7.2f, zc0 + 7f), y0 + 6f, y0 + 20f, 0f);
            NoEntryBox(Rect.MinMaxRect(x0, zc0, x0 + 7f, zc1), y0 + 6f, y0 + 20f, 0f);
            NoEntryBox(Rect.MinMaxRect(x1 - 7f, zc0 + 7.2f, x1, zc1), y0 + 6f, y0 + 20f, 0f);
            NoEntryBox(Rect.MinMaxRect(-8f, zc0 - 2.4f, 8f, zc0), y0 + 11f, y0 + 25f, 0f);
            NoEntryBox(Rect.MinMaxRect(fc.x - 3.3f, fc.z - 3.3f, fc.x + 3.3f, fc.z + 3.3f), y0 + 3.3f, y0 + 9f, 0f);
            NoEntryBox(Rect.MinMaxRect(fc.x - 3.0f, fc.z - 3.0f, fc.x + 3.0f, fc.z + 3.0f), y0 - 1f, y0 + 1f, 0f);
            _townSolids.Add(Rect.MinMaxRect(x0 - 1f, zc0 - 2.5f, x0 + 1f, zh1));
            _townSolids.Add(Rect.MinMaxRect(x1 - 1f, zc0 - 2.5f, x1 + 1f, zh1));
            _townSolids.Add(Rect.MinMaxRect(x0, zh1 - 1f, x1, zh1 + 1f));
            _ = go; _ = pc; _ = rng;
        }

        /// <summary>
        /// The minaret: a square stone shaft to 30 m, a balcony, an octagonal stage to 39 m, a second balcony, the
        /// lantern and its onion cap with the crescent. Inside, a square helix of ramps round a central core (each side
        /// a ramp under 26 degrees, each corner a landing) climbs to a door onto the lower balcony.
        /// </summary>
        private static void BuildMinaret(Transform group, int layer, Vector3 c, float y0, float bottom)
        {
            var m = new TownMesh();
            const float Half = 3.0f, T = 0.6f, Shaft = 30f, Core = 0.6f;
            float top = y0 + Shaft;
            // Shaft: four walls with a door at the base (west, onto the courtyard) and a door at the balcony (south).
            for (int s = 0; s < 4; s++)
            {
                var r = Rect.MinMaxRect(c.x - Half, c.z - Half, c.x + Half, c.z + Half);
                var (a0, b0) = FaceRun(r, s);
                var nrm = SideOut3(s);
                var holes = new List<Vector4>();
                float pv = y0 - bottom;
                if (s == 3) holes.Add(new Vector4(Half - 0.7f, Half + 0.7f, pv, pv + 2.4f));
                if (s == 1) holes.Add(new Vector4(Half - 0.6f, Half + 0.6f, pv + Shaft + 0.05f, pv + Shaft + 2.3f));
                // Slit windows climbing the shaft.
                for (float h = 5f; h < Shaft - 2f; h += 4.5f) holes.Add(new Vector4(Half - 0.15f, Half + 0.15f, pv + h, pv + h + 1.2f));
                m.WallWithHoles(_stone, a0 + Vector3.up * bottom, b0 + Vector3.up * bottom, top - bottom + 0.4f, nrm, holes);
                m.WallWithHoles(_stone, a0 - nrm * T + Vector3.up * bottom, b0 - nrm * T + Vector3.up * bottom, top - bottom + 0.4f, -nrm, holes);
                var u = (b0 - a0).normalized;
                foreach (var h in holes)
                {
                    foreach (float e in new[] { h.x, h.y })
                        m.Quad(_stone, a0 + u * e + Vector3.up * (bottom + h.z), a0 + u * e - nrm * T + Vector3.up * (bottom + h.z),
                               a0 + u * e - nrm * T + Vector3.up * (bottom + h.w), a0 + u * e + Vector3.up * (bottom + h.w), e == h.x ? u : -u);
                    m.Quad(_stone, a0 + u * h.x + Vector3.up * (bottom + h.w), a0 + u * h.y + Vector3.up * (bottom + h.w),
                           a0 + u * h.y - nrm * T + Vector3.up * (bottom + h.w), a0 + u * h.x - nrm * T + Vector3.up * (bottom + h.w), Vector3.down);
                    m.Quad(_stone, a0 + u * h.x + Vector3.up * (bottom + h.z), a0 + u * h.y + Vector3.up * (bottom + h.z),
                           a0 + u * h.y - nrm * T + Vector3.up * (bottom + h.z), a0 + u * h.x - nrm * T + Vector3.up * (bottom + h.z), Vector3.up);
                }
                // Carved bands every 6 m.
                for (float h = 6f; h < Shaft; h += 6f)
                    m.Box(_coping, (a0 + b0) * 0.5f + nrm * 0.05f + Vector3.up * (y0 + h), new Vector3(Half * 2f + 0.1f, 0.3f, 0.12f), Quaternion.LookRotation(nrm));
            }
            // Floor inside, and the helix.
            float inner = Half - T;
            m.Quad(_marble, new Vector3(c.x - inner, y0, c.z - inner), new Vector3(c.x + inner, y0, c.z - inner), new Vector3(c.x + inner, y0, c.z + inner), new Vector3(c.x - inner, y0, c.z + inner), Vector3.up);
            m.BoxMinMax(_stone, new Vector3(c.x - Core, y0, c.z - Core), new Vector3(c.x + Core, top, c.z + Core));
            float laneMid = (Core + inner) * 0.5f, laneW = inner - Core - 0.05f;
            float y = y0;
            // Corners walked anticlockwise from the south-west; each side is a ramp between two corner landings. The rise
            // per side is set so a full turn clears an agent's height under the turn above (3.1 m), and the count is a
            // multiple of four so the last ramp comes up the west side onto the landing at the balcony door, not under it.
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            int k = 0;
            float horizontal = 2f * laneMid - laneW;
            int ramps = Mathf.CeilToInt(Shaft / 0.8f);
            while (ramps % 4 != 0) ramps++;
            float rise = Shaft / ramps;
            for (int i = 0; i < ramps; i++)
            {
                var ca = corners[k % 4] * laneMid; var cb = corners[(k + 1) % 4] * laneMid;
                Vector3 pa = new Vector3(c.x + ca.x, y, c.z + ca.y), pb = new Vector3(c.x + cb.x, y + rise, c.z + cb.y);
                var dir = (pb - pa); dir.y = 0; dir.Normalize();
                // Landing at the start corner, then the ramp to the next corner.
                m.BoxMinMax(_stone, new Vector3(pa.x - laneW * 0.5f, y - 0.25f, pa.z - laneW * 0.5f), new Vector3(pa.x + laneW * 0.5f, y, pa.z + laneW * 0.5f));
                Ramp(m, _stone, pa + dir * (laneW * 0.5f), pb - dir * (laneW * 0.5f), laneW, y - 0.3f, walls: false);
                y += rise; k++;
            }
            _ = horizontal;
            // The last landing to the balcony door (south side, centre) at the shaft top.
            m.BoxMinMax(_stone, new Vector3(c.x - inner, top - 0.25f, c.z - inner), new Vector3(c.x + inner, top + 0.05f, c.z - Core));
            // Lower balcony: a ring platform with a parapet.
            m.BoxMinMax(_stone, new Vector3(c.x - Half - 1.9f, top - 0.3f, c.z - Half - 1.9f), new Vector3(c.x + Half + 1.9f, top + 0.05f, c.z + Half + 1.9f));
            foreach (var (a, b, o) in new[] { (new Vector2(-1, -1), new Vector2(1, -1), Vector2.down), (new Vector2(1, -1), new Vector2(1, 1), Vector2.right),
                                              (new Vector2(1, 1), new Vector2(-1, 1), Vector2.up), (new Vector2(-1, 1), new Vector2(-1, -1), Vector2.left) })
            {
                var pa = new Vector2(c.x, c.z) + a * (Half + 1.8f); var pb = new Vector2(c.x, c.z) + b * (Half + 1.8f);
                SolidWall(m, _stone, pa, pb, top, top + 1.05f, 0.2f);
                _ = o;
            }
            // Octagonal upper stage, upper balcony, lantern, cap.
            var oc = new Vector3(c.x, top + 0.05f, c.z);
            m.Cylinder(_stone, oc, Half * 0.92f, 9f, 8, true);
            m.Cylinder(_coping, oc + Vector3.up * 9f, Half * 0.92f + 1.0f, 0.3f, 8, true, true);
            m.Cylinder(_stone, oc + Vector3.up * 9.3f, Half * 0.92f + 0.95f, 0.9f, 8, false);
            m.Cylinder(_stucco, oc + Vector3.up * 9.3f, 1.6f, 4f, 12, true);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                m.Box(_glassDark, oc + Vector3.up * 11.3f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.61f, new Vector3(0.5f, 1.4f, 0.04f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))));
            }
            Dome(m, _domeTile, oc + Vector3.up * 13.3f, 1.9f, 3.2f, true, 20);
            Finial(m, oc + Vector3.up * 16.4f, 1.2f);
            var go = m.Build(group, "Minaret", layer, "Concrete");
            _ = go;
            // The upper stage and the shaft's solid core and walls: off the bake. The balcony stays walkable.
            NoEntryBox(Rect.MinMaxRect(c.x - Half, c.z - Half, c.x + Half, c.z + Half), top + 0.3f, top + 30f, -1.2f);
        }

        // ==================================================================
        // The citadel
        // ==================================================================
        /// <summary>
        /// The old fort on the hill: curtain walls nine metres high with a wall-walk and merlons, round towers at the
        /// corners and the middle of the long sides, a gatehouse at the top of the ramp, a keep in the courtyard you can
        /// climb, ruined barracks, a cistern, and one stretch of wall that has fallen. Ramps inside go up to the walk.
        /// </summary>
        private static void BuildCitadel(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var r = CitadelRect;
            float y0 = SampleTownBase(CitadelCentre.x, CitadelCentre.y) + 0.05f;
            var (low, _) = GroundRange(r);
            float bottom = low - 1.5f;
            const float H = 9f, T = 3f;

            // The courtyard floor (packed earth is the terrain; a stone paving band inside the gate).
            m.Quad(_paversPlaza, new Vector3(292f, y0 + 0.02f, r.yMin + T), new Vector3(308f, y0 + 0.02f, r.yMin + T),
                   new Vector3(308f, y0 + 0.02f, r.yMin + 26f), new Vector3(292f, y0 + 0.02f, r.yMin + 26f), Vector3.up);

            // Curtain walls: solid, battered, with the south wall split for the gate and the east wall broken in one place.
            var corners = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            var outs = new[] { Vector2.down, Vector2.right, Vector2.up, Vector2.left };
            for (int s = 0; s < 4; s++)
            {
                Vector2 a = corners[s], b = corners[(s + 1) % 4], o = outs[s];
                Vector2 inset = -o * (T * 0.5f);
                var segs = new List<(Vector2, Vector2)>();
                if (s == 0) { segs.Add((a, new Vector2(295f, a.y))); segs.Add((new Vector2(305f, a.y), b)); }
                else if (s == 1) { segs.Add((a, new Vector2(a.x, 340f))); segs.Add((new Vector2(a.x, 352f), b)); }
                else segs.Add((a, b));
                foreach (var (p, q) in segs)
                {
                    SolidWall(m, _castle, p + inset, q + inset, bottom, y0 + H, T, noEntryTop: y0 + H - 0.4f);
                    Merlons(m, _castle, p + inset, q + inset, y0 + H, T, o);
                }
                // A batter at the foot: the lowest course stepped out.
                SolidWall(m, _castle, a + inset - o * 0.6f, b + inset - o * 0.6f, bottom, y0 + 1.2f, T + 1.2f, noEntryTop: y0 + 1.6f);
            }
            // The breach: rubble where the east wall came down.
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector2(r.xMax - 1.5f + Rand(rng, -2f, 3f), 346f + Rand(rng, -5f, 5f));
                var mesh = BoulderMesh(rng.Next(1000), 0.45f, 0.4f);
                float rx = Rand(rng, 2.5f, 4.5f), rz = Rand(rng, 2.5f, 4.5f);
                var rub = MeshObject(group, "Rubble", mesh, _castle, new Vector3(p.x, LowestGroundIn(p.x, p.y, Mathf.Max(rx, rz) * 0.6f) - 0.2f, p.y),
                                     Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), new Vector3(rx, Rand(rng, 1.0f, 2.2f), rz), layer, "Concrete");
                NoStanding(rub);
            }

            // Round towers at the corners and mid-long-sides, their tops level with the wall-walk.
            var towers = new List<Vector2> { corners[0], corners[1], corners[2], corners[3], new Vector2(r.xMin, r.center.y), new Vector2(r.xMax, r.center.y + 20f), new Vector2(r.center.x, r.yMax) };
            foreach (var tc in towers)
            {
                // The tower's top is level with the wall-walk, so the walk runs straight across it.
                m.Cylinder(_castle, new Vector3(tc.x, bottom, tc.y), 6.2f, y0 + H - bottom, 28, true);
                NoEntryBox(Rect.MinMaxRect(tc.x - 6.2f, tc.y - 6.2f, tc.x + 6.2f, tc.y + 6.2f), bottom - 1f, y0 + H - 0.4f, 0.2f);
                for (int i = 0; i < 14; i++)
                {
                    float a = i * Mathf.PI * 2f / 14f;
                    var p = new Vector3(tc.x + Mathf.Cos(a) * 5.9f, y0 + H + 0.55f, tc.y + Mathf.Sin(a) * 5.9f);
                    m.Box(_castle, p, new Vector3(1.3f, 1.1f, 0.6f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))));
                }
                // Arrow slits.
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2f / 5f + 0.3f;
                    m.Box(_glassDark, new Vector3(tc.x + Mathf.Cos(a) * 6.22f, y0 + 4f, tc.y + Mathf.Sin(a) * 6.22f), new Vector3(0.25f, 1.6f, 0.04f),
                          Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))));
                }
            }
            // Short ramps from the wall-walk up the 1.5 m onto each tower top are not needed: the tower tops are a step
            // up of 1.5 m, too high, so the walk and the tower meet level: fill a 3 m apron at walk height into each tower.

            // The gatehouse: two square towers flanking a pointed arch through the south wall.
            m.BoxMinMax(_castle, new Vector3(287f, bottom, r.yMin - 4f), new Vector3(295f, y0 + H + 3f, r.yMin + T + 1f));
            m.BoxMinMax(_castle, new Vector3(305f, bottom, r.yMin - 4f), new Vector3(313f, y0 + H + 3f, r.yMin + T + 1f));
            m.WallWithArches(_castle, new Vector3(295f, y0, r.yMin - 4f), new Vector3(305f, y0, r.yMin - 4f), H + 3f, Vector3.back, new List<Vector3> { new Vector3(5f, 7f, 4.5f) }, true);
            m.WallWithArches(_castle, new Vector3(295f, y0, r.yMin + T + 1f), new Vector3(305f, y0, r.yMin + T + 1f), H + 3f, Vector3.forward, new List<Vector3> { new Vector3(5f, 7f, 4.5f) }, true);
            m.ArchSoffit(_castle, new Vector3(295f, y0, r.yMin - 4f), Vector3.right, Vector3.forward, 5f, 7f, 4.5f, T + 5f, true);
            m.BoxMinMax(_castle, new Vector3(295f, y0 + H + 2.5f, r.yMin - 4f), new Vector3(305f, y0 + H + 3f, r.yMin + T + 1f));
            NoEntryBox(Rect.MinMaxRect(287f, r.yMin - 4f, 295f, r.yMin + T + 1f), bottom - 1f, y0 + H + 6f, 0f);
            NoEntryBox(Rect.MinMaxRect(305f, r.yMin - 4f, 313f, r.yMin + T + 1f), bottom - 1f, y0 + H + 6f, 0f);
            NoEntryBox(Rect.MinMaxRect(295f, r.yMin - 4f, 305f, r.yMin + T + 1f), y0 + 9.5f, y0 + H + 6f, 0f);
            Merlons(m, _castle, new Vector2(287f, r.yMin - 4f), new Vector2(313f, r.yMin - 4f), y0 + H + 3f, 1f, Vector2.down);
            // Wooden gates, one leaf open.
            m.Box(_doorWood, new Vector3(298.2f, y0 + 3f, r.yMin - 0.5f), new Vector3(0.2f, 6f, 3.4f), Quaternion.identity);

            // Wall-walk ramps inside the south and north walls (24 degrees, 2.6 m wide), from the courtyard to the walk.
            float walk = y0 + H;
            float run = H / Mathf.Tan(24f * Mathf.Deg2Rad);
            // No rails: the walk is stepped onto sideways from the ramp's top, where it runs along the wall.
            Ramp(m, _castle, new Vector3(r.xMin + T + 4f + run, y0, r.yMin + T + 1.35f), new Vector3(r.xMin + T + 4f, walk, r.yMin + T + 1.35f), 2.6f, y0 - 0.5f, walls: false);
            Ramp(m, _castle, new Vector3(r.xMax - T - 4f - run, y0, r.yMax - T - 1.35f), new Vector3(r.xMax - T - 4f, walk, r.yMax - T - 1.35f), 2.6f, y0 - 0.5f, walls: false);

            // The keep: a square tower in the courtyard, a ramp inside to its roof.
            var kc = new Vector2(318f, 368f);
            BuildKeep(m, kc, y0, bottom);

            // Barracks: roofless shells of rooms along the west wall.
            for (int i = 0; i < 3; i++)
            {
                float z = r.yMin + 20f + i * 18f;
                var room = Rect.MinMaxRect(r.xMin + T, z, r.xMin + T + 9f, z + 14f);
                for (int s = 0; s < 4; s++)
                {
                    if (s == 3) continue;    // backs onto the curtain wall
                    var (a0, b0) = FaceRun(room, s);
                    var n = SideOut3(s);
                    var holes = new List<Vector4>();
                    if (s == 2) holes.Add(new Vector4(6f, 7.4f, 0.4f, 2.6f));
                    float h = Rand(rng, 2.2f, 3.6f);
                    m.WallWithHoles(_castle, a0 + Vector3.up * (y0 - 0.4f), b0 + Vector3.up * (y0 - 0.4f), h, n, holes);
                    m.WallWithHoles(_castle, a0 - n * 0.5f + Vector3.up * (y0 - 0.4f), b0 - n * 0.5f + Vector3.up * (y0 - 0.4f), h, -n, holes);
                    m.Box(_castle, (a0 + b0) * 0.5f - n * 0.25f + Vector3.up * (y0 - 0.4f + h + 0.05f), new Vector3(Vector3.Distance(a0, b0), 0.1f, 0.5f), Quaternion.LookRotation(n));
                }
            }
            // A cistern: a stone-rimmed square of dark water.
            var cc = new Vector3(272f, y0, 375f);
            m.BoxMinMax(_castle, cc + new Vector3(-4f, -0.2f, -4f), cc + new Vector3(4f, 0.7f, -3.4f));
            m.BoxMinMax(_castle, cc + new Vector3(-4f, -0.2f, 3.4f), cc + new Vector3(4f, 0.7f, 4f));
            m.BoxMinMax(_castle, cc + new Vector3(-4f, -0.2f, -3.4f), cc + new Vector3(-3.4f, 0.7f, 3.4f));
            m.BoxMinMax(_castle, cc + new Vector3(3.4f, -0.2f, -3.4f), cc + new Vector3(4f, 0.7f, 3.4f));
            m.Quad(_townWater, cc + new Vector3(-3.4f, 0.3f, -3.4f), cc + new Vector3(3.4f, 0.3f, -3.4f), cc + new Vector3(3.4f, 0.3f, 3.4f), cc + new Vector3(-3.4f, 0.3f, 3.4f), Vector3.up);
            NoEntryBox(Rect.MinMaxRect(cc.x - 3.4f, cc.z - 3.4f, cc.x + 3.4f, cc.z + 3.4f), y0 - 2f, y0 + 2f, 0f);

            m.Build(group, "Citadel", layer, "Concrete");

            // Its stores: crates, ammunition, a searchlight on the keep, a flag.
            string[] pool = { "wooden_military_crate", "old_military_crate", "ammo_box", "Barrel_01", "plastic_jerrycan" };
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 14f, r.xMax - 10f), Rand(rng, r.yMin + 26f, r.yMax - 8f));
                if (Vector2.Distance(p, kc) < 12f) continue;
                PlaceTown(props, Prop(pool[rng.Next(pool.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f));
            }
            PlaceTown(props, Prop("portable_searchlight"), new Vector3(kc.x + 5f, y0 + 20.1f, kc.y + 5f), 200f, 1.4f);
            Flag(group, layer, new Vector3(kc.x - 5f, y0 + 20.1f, kc.y - 5f), 6f);

            // Off the bake: tower tops and merlons are left to the minimum region size; the solid walls are solid.
            _townSolids.Add(Rect.MinMaxRect(r.xMin - 6f, r.yMin - 6f, r.xMax + 6f, r.yMin + T));
            _townSolids.Add(Rect.MinMaxRect(r.xMin - 6f, r.yMax - T, r.xMax + 6f, r.yMax + 6f));
        }

        private static void BuildKeep(TownMesh m, Vector2 kc, float y0, float bottom)
        {
            const float Half = 8f, T = 1.2f, Hk = 20f;
            var r = Rect.MinMaxRect(kc.x - Half, kc.y - Half, kc.x + Half, kc.y + Half);
            for (int s = 0; s < 4; s++)
            {
                var (a0, b0) = FaceRun(r, s);
                var n = SideOut3(s);
                var holes = new List<Vector4>();
                float pv = y0 - bottom;
                if (s == 1) holes.Add(new Vector4(Half - 0.9f, Half + 0.9f, pv, pv + 2.6f));
                for (float h = 5f; h < Hk - 2f; h += 5f)
                    for (float u0 = 3f; u0 < Half * 2f - 2f; u0 += 5f) holes.Add(new Vector4(u0, u0 + 0.35f, pv + h, pv + h + 1.4f));
                m.WallWithHoles(_castle, a0 + Vector3.up * bottom, b0 + Vector3.up * bottom, y0 + Hk - bottom, n, holes);
                m.WallWithHoles(_castle, a0 - n * T + Vector3.up * bottom, b0 - n * T + Vector3.up * bottom, y0 + Hk - bottom, -n, holes);
                var u = (b0 - a0).normalized;
                foreach (var h in holes)
                {
                    foreach (float e in new[] { h.x, h.y })
                        m.Quad(_castle, a0 + u * e + Vector3.up * (bottom + h.z), a0 + u * e - n * T + Vector3.up * (bottom + h.z),
                               a0 + u * e - n * T + Vector3.up * (bottom + h.w), a0 + u * e + Vector3.up * (bottom + h.w), e == h.x ? u : -u);
                    m.Quad(_castle, a0 + u * h.x + Vector3.up * (bottom + h.w), a0 + u * h.y + Vector3.up * (bottom + h.w),
                           a0 + u * h.y - n * T + Vector3.up * (bottom + h.w), a0 + u * h.x - n * T + Vector3.up * (bottom + h.w), Vector3.down);
                }
                Merlons(m, _castle, new Vector2(a0.x, a0.z), new Vector2(b0.x, b0.z), y0 + Hk, T, new Vector2(n.x, n.z));
            }
            // Inside: floor, then ramps up the walls in a square spiral to a roof slab with a hatch opening.
            float inner = Half - T;
            m.Quad(_castle, new Vector3(r.xMin + T, y0 + 0.02f, r.yMin + T), new Vector3(r.xMax - T, y0 + 0.02f, r.yMin + T),
                   new Vector3(r.xMax - T, y0 + 0.02f, r.yMax - T), new Vector3(r.xMin + T, y0 + 0.02f, r.yMax - T), Vector3.up);
            float y = y0, w = 2.2f;
            float rise = Mathf.Tan(23f * Mathf.Deg2Rad) * (inner * 2f - w * 2f);
            var path = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            int k = 0;
            while (y + rise <= y0 + Hk - 0.2f)
            {
                var a = kc + path[k % 4] * (inner - w * 0.5f); var b = kc + path[(k + 1) % 4] * (inner - w * 0.5f);
                var dir = (b - a).normalized;
                var pa = new Vector3(a.x, y, a.y) + new Vector3(dir.x, 0, dir.y) * (w * 0.5f);
                var pb = new Vector3(b.x, y + rise, b.y) - new Vector3(dir.x, 0, dir.y) * (w * 0.5f);
                m.BoxMinMax(_castle, new Vector3(a.x - w * 0.5f, y - 0.3f, a.y - w * 0.5f), new Vector3(a.x + w * 0.5f, y, a.y + w * 0.5f));
                Ramp(m, _castle, pa, pb, w, y - 0.4f, walls: false);
                y += rise; k++;
            }
            // Roof at the level the ramps reached, open over the last flight and its corner (the stairwell), so the climb
            // has headroom all the way and comes out flush with the roof.
            float roofY = y;
            var cA = kc + path[(k - 1 + 4) % 4] * (inner - w * 0.5f);
            var cB = kc + path[k % 4] * (inner - w * 0.5f);
            m.BoxMinMax(_castle, new Vector3(cB.x - w * 0.5f, roofY - 0.3f, cB.y - w * 0.5f), new Vector3(cB.x + w * 0.5f, roofY, cB.y + w * 0.5f));
            var well = Rect.MinMaxRect(Mathf.Min(cA.x, cB.x) - w * 0.5f, Mathf.Min(cA.y, cB.y) - w * 0.5f, Mathf.Max(cA.x, cB.x) + w * 0.5f, Mathf.Max(cA.y, cB.y) + w * 0.5f);
            var I = Rect.MinMaxRect(r.xMin + T, r.yMin + T, r.xMax - T, r.yMax - T);
            foreach (var piece in new[]
            {
                Rect.MinMaxRect(I.xMin, I.yMin, well.xMin, I.yMax), Rect.MinMaxRect(well.xMax, I.yMin, I.xMax, I.yMax),
                Rect.MinMaxRect(well.xMin, I.yMin, well.xMax, well.yMin), Rect.MinMaxRect(well.xMin, well.yMax, well.xMax, I.yMax),
            })
                if (piece.width > 0.05f && piece.height > 0.05f)
                    m.BoxMinMax(_castle, new Vector3(piece.xMin, roofY - 0.3f, piece.yMin), new Vector3(piece.xMax, roofY, piece.yMax));
            // The walls run to y0 + Hk (above), so they stand round the roof as a parapet with merlons.
            _ = Hk;
            // The walls stand 1.2 m above the roof as a parapet with merlons (built above), so walls run to y0 + Hk.
        }

        private static void Flag(Transform group, int layer, Vector3 at, float height)
        {
            var m = new TownMesh();
            m.Cylinder(_steel, at, 0.05f, height, 8);
            var cloth = TownFlat("FlagCloth", new Color(0.12f, 0.42f, 0.22f), 0.1f);
            m.Box(cloth, at + new Vector3(0.75f, height - 0.55f, 0f), new Vector3(1.5f, 0.95f, 0.02f), Quaternion.Euler(0f, 0f, 0f));
            m.Build(group, "Flag", layer, "Metal", collider: false);
        }

        // ==================================================================
        // The palace
        // ==================================================================
        /// <summary>
        /// The governor's palace: a crenellated white wall round a formal garden -- an axial path from the gatehouse to
        /// the palace, an octagonal fountain where the paths cross, rows of palms and planted beds -- and the palace
        /// itself, two floors behind a pointed arcade, with a domed central hall you can walk into.
        /// </summary>
        private static void BuildPalace(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var r = PalaceRect;
            float y0 = SampleTownBase(r.center.x, r.center.y) + 0.15f;
            var (low, _) = GroundRange(r);
            float bottom = low - 0.6f;
            const float WallH = 4.2f, T = 0.6f;
            float gx = r.center.x;

            // Perimeter wall with the gate (8 m) in the south side, crenellated.
            var corners = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            var outs = new[] { Vector2.down, Vector2.right, Vector2.up, Vector2.left };
            for (int s = 0; s < 4; s++)
            {
                Vector2 a = corners[s], b = corners[(s + 1) % 4], o = outs[s];
                var segs = new List<(Vector2, Vector2)>();
                if (s == 0) { segs.Add((a, new Vector2(gx - 4f, a.y))); segs.Add((new Vector2(gx + 4f, a.y), b)); }
                else segs.Add((a, b));
                foreach (var (p, q) in segs)
                {
                    SolidWall(m, _stucco, p - o * (T * 0.5f), q - o * (T * 0.5f), bottom, y0 + WallH, T, noEntryTop: y0 + WallH + 2f);
                    Merlons(m, _stucco, p - o * (T * 0.5f), q - o * (T * 0.5f), y0 + WallH, T, o, 0.6f, 0.7f);
                }
            }
            // Gatehouse: two pylons and a lintel with a small dome on each, gold finials.
            m.BoxMinMax(_stone, new Vector3(gx - 7f, bottom, r.yMin - 1.5f), new Vector3(gx - 4f, y0 + 7f, r.yMin + 1.5f));
            m.BoxMinMax(_stone, new Vector3(gx + 4f, bottom, r.yMin - 1.5f), new Vector3(gx + 7f, y0 + 7f, r.yMin + 1.5f));
            m.WallWithArches(_stone, new Vector3(gx - 4f, y0, r.yMin - 1.5f), new Vector3(gx + 4f, y0, r.yMin - 1.5f), 8.5f, Vector3.back, new List<Vector3> { new Vector3(4f, 8f, 4.8f) }, true);
            m.WallWithArches(_stone, new Vector3(gx - 4f, y0, r.yMin + 1.5f), new Vector3(gx + 4f, y0, r.yMin + 1.5f), 8.5f, Vector3.forward, new List<Vector3> { new Vector3(4f, 8f, 4.8f) }, true);
            m.ArchSoffit(_stone, new Vector3(gx - 4f, y0, r.yMin - 1.5f), Vector3.right, Vector3.forward, 4f, 8f, 4.8f, 3f, true);
            m.BoxMinMax(_stone, new Vector3(gx - 7f, y0 + 8.5f, r.yMin - 1.5f), new Vector3(gx + 7f, y0 + 9f, r.yMin + 1.5f));
            foreach (float sx in new[] { -5.5f, 5.5f })
            {
                Dome(m, _domeTile, new Vector3(gx + sx, y0 + 7f, r.yMin), 1.3f, 1.6f, true, 16);
                Finial(m, new Vector3(gx + sx, y0 + 8.6f, r.yMin), 0.7f);
            }

            // Garden: the axial path, a cross path, beds, the fountain.
            float pathY = y0 + 0.03f;
            var walk = TownMat("GardenPath", "pavement_02", new Color(1.0f, 0.96f, 0.90f), 3f);
            m.Quad(walk, new Vector3(gx - 3f, pathY, r.yMin + 1.5f), new Vector3(gx + 3f, pathY, r.yMin + 1.5f), new Vector3(gx + 3f, pathY, r.yMax - 36f), new Vector3(gx - 3f, pathY, r.yMax - 36f), Vector3.up);
            float cz = (r.yMin + r.yMax - 36f) * 0.5f;
            m.Quad(walk, new Vector3(r.xMin + 6f, pathY, cz - 2.5f), new Vector3(r.xMax - 6f, pathY, cz - 2.5f), new Vector3(r.xMax - 6f, pathY, cz + 2.5f), new Vector3(r.xMin + 6f, pathY, cz + 2.5f), Vector3.up);
            var fc = new Vector3(gx, y0, cz);
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var p0 = fc + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 6f; var p1 = fc + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 6f;
                SolidWall(m, _marble, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), y0 - 0.2f, y0 + 0.55f, 0.4f);
            }
            m.Disc(_townWater, fc + Vector3.up * 0.4f, 5.8f, 32, Vector3.up);
            m.Cylinder(_marble, fc, 0.9f, 1.4f, 16, true);
            m.Cylinder(_marble, fc + Vector3.up * 1.4f, 1.6f, 0.25f, 16, true, true);
            m.Cylinder(_marble, fc + Vector3.up * 1.65f, 0.3f, 0.8f, 12, true);
            NoEntryBox(Rect.MinMaxRect(fc.x - 6f, fc.z - 6f, fc.x + 6f, fc.z + 6f), y0 - 1f, y0 + 3f, 0f);

            // Rows of palms along the paths, beds of shrubs between.
            for (float z = r.yMin + 10f; z < r.yMax - 40f; z += 9f)
                foreach (float sx in new[] { -6f, 6f })
                {
                    if (Mathf.Abs(z - cz) < 9f) continue;
                    PlaceTown(props, PackPrefab("Palm_SingleStraight"), new Vector3(gx + sx, y0, z), Rand(rng, 0f, 360f), Rand(rng, 8f, 10f), navBlock: false);
                }
            // The lighter scans only (the searsia and succulent scans are 200-700k triangles each).
            string[] shrubs = { "wild_rooibos_bush", "wild_rooibos_bush", "dry_branches_medium_01" };
            for (int i = 0; i < 60; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 6f, r.xMax - 6f), Rand(rng, r.yMin + 6f, r.yMax - 40f));
                if (Mathf.Abs(p.x - gx) < 5f || Mathf.Abs(p.y - cz) < 4.5f || Vector2.Distance(p, new Vector2(fc.x, fc.z)) < 8f) continue;
                PlaceTown(props, Prop(shrubs[rng.Next(shrubs.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f), navBlock: false);
            }

            // The palace: 70 x 26 m, two floors (5 m and 4.5 m), a pointed arcade across the ground floor, a domed hall.
            float px0 = gx - 35f, px1 = gx + 35f, pz0 = r.yMax - 32f, pz1 = r.yMax - 6f;
            const float F0 = 5f, F1 = 4.5f, Par = 1.2f;
            float top = y0 + F0 + F1;
            var foot = Rect.MinMaxRect(px0, pz0, px1, pz1);
            for (int s = 0; s < 4; s++)
            {
                var (a0, b0) = FaceRun(foot, s);
                var n = SideOut3(s);
                float len = Vector3.Distance(a0, b0);
                var holes = new List<Vector4>();
                float pv = y0 - bottom;
                int bays = Mathf.RoundToInt(len / 4.5f);
                for (int k = 0; k < bays; k++)
                {
                    float cu = len * (k + 0.5f) / bays;
                    if (s != 1) holes.Add(new Vector4(cu - 0.7f, cu + 0.7f, pv + 1.4f, pv + 3.6f));
                    holes.Add(new Vector4(cu - 0.65f, cu + 0.65f, pv + F0 + 1.0f, pv + F0 + 3.2f));
                }
                if (s == 1)
                {
                    // The south face's ground floor is the arcade (built in front); doors through to the hall.
                    holes.Add(new Vector4(len * 0.5f - 2f, len * 0.5f + 2f, pv, pv + 4f));
                    holes.Add(new Vector4(len * 0.25f - 0.8f, len * 0.25f + 0.8f, pv, pv + 2.6f));
                    holes.Add(new Vector4(len * 0.75f - 0.8f, len * 0.75f + 0.8f, pv, pv + 2.6f));
                }
                m.WallWithHoles(_stucco, a0 + Vector3.up * bottom, b0 + Vector3.up * bottom, top + Par - bottom, n, holes);
                var u = (b0 - a0) / len;
                foreach (var h in holes)
                {
                    var p0 = a0 + u * h.x + Vector3.up * (bottom + h.z); var p1 = a0 + u * h.y + Vector3.up * (bottom + h.z);
                    var q0 = a0 + u * h.x + Vector3.up * (bottom + h.w); var q1 = a0 + u * h.y + Vector3.up * (bottom + h.w);
                    var inn = -n * 0.35f;
                    m.Quad(_stucco, p0, p1, p1 + inn, p0 + inn, Vector3.up); m.Quad(_stucco, q0, q1, q1 + inn, q0 + inn, Vector3.down);
                    m.Quad(_stucco, p0, q0, q0 + inn, p0 + inn, u); m.Quad(_stucco, p1, q1, q1 + inn, p1 + inn, -u);
                    bool door = h.z - pv < 0.2f;
                    if (!door) m.Quad(_glassDark, p0 + inn, p1 + inn, q1 + inn, q0 + inn, n);
                }
                m.Box(_coping, (a0 + b0) * 0.5f + Vector3.up * (y0 + F0) + n * 0.05f, new Vector3(len + 0.1f, 0.3f, 0.12f), Quaternion.LookRotation(n));
                Merlons(m, _stucco, new Vector2(a0.x, a0.z), new Vector2(b0.x, b0.z), top + Par, 0.4f, new Vector2(n.x, n.z), 0.5f, 0.6f);
            }
            m.BoxMinMax(_roof, new Vector3(px0, top, pz0), new Vector3(px1, top + 0.1f, pz1));
            // The arcade in front of the south face.
            {
                var a = new Vector3(px0 + 4f, 0, pz0 - 4f); var b = new Vector3(px1 - 4f, 0, pz0 - 4f);
                float len = b.x - a.x; int spans = 12; var arches = new List<Vector3>();
                for (int i = 0; i < spans; i++) arches.Add(new Vector3(len * (i + 0.5f) / spans, len / spans - 1.1f, 3.4f));
                m.WallWithArches(_stucco, a + Vector3.up * y0, b + Vector3.up * y0, F0, Vector3.back, arches, true);
                m.WallWithArches(_stucco, a + Vector3.forward * 0.7f + Vector3.up * y0, b + Vector3.forward * 0.7f + Vector3.up * y0, F0, Vector3.forward, arches, true);
                foreach (var ar in arches) m.ArchSoffit(_stucco, a + Vector3.up * y0, Vector3.right, Vector3.forward, ar.x, ar.y, ar.z, 0.7f, true);
                m.BoxMinMax(_stucco, new Vector3(a.x, y0 + F0, a.z), new Vector3(b.x, y0 + F0 + 0.4f, pz0));
                m.Quad(_marble, new Vector3(a.x, y0 + 0.02f, a.z), new Vector3(b.x, y0 + 0.02f, a.z), new Vector3(b.x, y0 + 0.02f, pz0), new Vector3(a.x, y0 + 0.02f, pz0), Vector3.up);
            }
            // The central hall inside: marble floor, ceiling, a carpet; and its dome above the roof.
            var hall = Rect.MinMaxRect(gx - 9f, pz0 + 0.35f, gx + 9f, pz1 - 0.35f);
            m.Quad(_marble, new Vector3(hall.xMin, y0 + 0.02f, hall.yMin), new Vector3(hall.xMax, y0 + 0.02f, hall.yMin), new Vector3(hall.xMax, y0 + 0.02f, hall.yMax), new Vector3(hall.xMin, y0 + 0.02f, hall.yMax), Vector3.up);
            m.Quad(_stucco, new Vector3(hall.xMin, y0 + F0 - 0.2f, hall.yMin), new Vector3(hall.xMax, y0 + F0 - 0.2f, hall.yMin), new Vector3(hall.xMax, y0 + F0 - 0.2f, hall.yMax), new Vector3(hall.xMin, y0 + F0 - 0.2f, hall.yMax), Vector3.down);
            foreach (float sx in new[] { -1f, 1f })
                m.Quad(_stucco, new Vector3(gx + sx * 9f, y0, hall.yMin), new Vector3(gx + sx * 9f, y0, hall.yMax), new Vector3(gx + sx * 9f, y0 + F0, hall.yMax), new Vector3(gx + sx * 9f, y0 + F0, hall.yMin), new Vector3(-sx, 0, 0));
            m.Quad(_stucco, new Vector3(hall.xMin, y0, hall.yMax), new Vector3(hall.xMax, y0, hall.yMax), new Vector3(hall.xMax, y0 + F0, hall.yMax), new Vector3(hall.xMin, y0 + F0, hall.yMax), Vector3.back);
            // The front wall seen from inside the hall, with its door (the outer face alone is invisible from behind).
            m.WallWithHoles(_stucco, new Vector3(hall.xMax, y0, hall.yMin), new Vector3(hall.xMin, y0, hall.yMin), F0, Vector3.forward,
                            new List<Vector4> { new Vector4(9f - 2f, 9f + 2f, 0f, 4f) });
            var cell = new Rect(0f, 0.5f, 0.5f, 0.5f);
            float cy = y0 + 0.04f;
            m.QuadUV(_carpet, new Vector3(gx - 5f, cy, pz0 + 4f), new Vector3(gx + 5f, cy, pz0 + 4f), new Vector3(gx + 5f, cy, pz1 - 4f), new Vector3(gx - 5f, cy, pz1 - 4f), Vector3.up,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            m.Cylinder(_stone, new Vector3(gx, top + 0.1f, (pz0 + pz1) * 0.5f), 7.5f, 2.2f, 32, false);
            Dome(m, _domeTile, new Vector3(gx, top + 2.3f, (pz0 + pz1) * 0.5f), 7.4f, 6.5f, true, 40);
            Finial(m, new Vector3(gx, top + 8.8f, (pz0 + pz1) * 0.5f), 1.4f);

            m.Build(group, "Palace", layer, "Concrete");

            // The palace's closed parts off the bake: everything but the central hall (and the roof, which has no way up).
            NoEntryBox(Rect.MinMaxRect(px0, pz0, gx - 9f, pz1), bottom, top + 12f, 0.3f);
            NoEntryBox(Rect.MinMaxRect(gx + 9f, pz0, px1, pz1), bottom, top + 12f, 0.3f);
            NoEntryBox(Rect.MinMaxRect(gx - 9f, pz0, gx + 9f, pz1), y0 + F0 - 0.4f, top + 12f, 0f);
            NoEntryBox(Rect.MinMaxRect(px0 + 4f, pz0 - 4f, px1 - 4f, pz0), y0 + F0 - 0.3f, top + 4f, 0f);
            NoEntryBox(Rect.MinMaxRect(gx - 7f, r.yMin - 1.5f, gx + 7f, r.yMin + 1.5f), y0 + 6f, y0 + 14f, 0f);
            _townSolids.Add(foot);
            // A guard's car and the governor's.
            _yardCars.Add(new Vector4(gx + 18f, 0f, pz0 - 12f, 90f));
            _palaceCar = new Vector4(gx - 18f, 0f, pz0 - 12f, 90f);
            // Guard booths either side of the gate, outside.
            foreach (float sx in new[] { -10f, 10f })
            {
                var bm = new TownMesh();
                var bc = new Vector3(gx + sx, GroundHeightAt(gx + sx, r.yMin - 4f), r.yMin - 4f);
                bm.BoxMinMax(_stucco, bc + new Vector3(-1f, -0.3f, -1f), bc + new Vector3(1f, 2.6f, 1f));
                bm.BoxMinMax(_roof, bc + new Vector3(-1.2f, 2.6f, -1.2f), bc + new Vector3(1.2f, 2.8f, 1.2f));
                bm.Quad(_glassDark, bc + new Vector3(-0.6f, 1.1f, -1.01f), bc + new Vector3(0.6f, 1.1f, -1.01f), bc + new Vector3(0.6f, 2.1f, -1.01f), bc + new Vector3(-0.6f, 2.1f, -1.01f), Vector3.back);
                bm.Build(group, "GuardBooth", layer, "Concrete");
                _townSolids.Add(Rect.MinMaxRect(bc.x - 1.2f, bc.z - 1.2f, bc.x + 1.2f, bc.z + 1.2f));
            }
        }

        /// <summary>Where the governor's car is parked (FPSKitTownLife puts the sports car there).</summary>
        private static Vector4 _palaceCar;

        // ==================================================================
        // The clock tower
        // ==================================================================
        /// <summary>
        /// The clock tower on the roundabout's island: a stone shaft on a stepped plinth, four clock faces with Eastern
        /// Arabic numerals and hands at twenty to five, a gallery, a small ribbed dome and a finial. Low railings and
        /// four palms round it.
        /// </summary>
        private static void BuildClockTower(Transform group, int layer, Transform props)
        {
            var m = new TownMesh();
            var c = new Vector3(RoundaboutCentre.x, TownRoadY(RoundaboutCentre.x, RoundaboutCentre.y) + KerbHeight + 0.1f, RoundaboutCentre.y);
            m.BoxMinMax(_stone, c + new Vector3(-4f, -0.4f, -4f), c + new Vector3(4f, 0.5f, 4f));
            m.BoxMinMax(_stone, c + new Vector3(-3.2f, 0.5f, -3.2f), c + new Vector3(3.2f, 1.2f, 3.2f));
            m.BoxMinMax(_stone, c + new Vector3(-2.3f, 1.2f, -2.3f), c + new Vector3(2.3f, 18f, 2.3f));
            for (float h = 4f; h < 17f; h += 4.5f)
                m.BoxMinMax(_coping, c + new Vector3(-2.4f, h, -2.4f), c + new Vector3(2.4f, h + 0.3f, 2.4f));
            // The clock stage, slightly wider, with the faces.
            m.BoxMinMax(_stone, c + new Vector3(-2.7f, 18f, -2.7f), c + new Vector3(2.7f, 22.5f, 2.7f));
            for (int s = 0; s < 4; s++)
            {
                var n = SideOut3(s);
                var right = Vector3.Cross(Vector3.up, n);
                var fcen = c + Vector3.up * 20.25f + n * 2.71f;
                const float R = 1.7f;
                m.QuadUV(_clockFace, fcen - right * R - Vector3.up * R, fcen + right * R - Vector3.up * R, fcen + right * R + Vector3.up * R, fcen - right * R + Vector3.up * R, n,
                         new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                // Hands: 4:40.
                var rot = Quaternion.LookRotation(n);
                float hourA = (4f + 40f / 60f) / 12f * 360f, minA = 40f / 60f * 360f;
                m.Box(_steel, fcen + n * 0.02f + (Quaternion.AngleAxis(-hourA, n) * Vector3.up) * 0.45f, new Vector3(0.08f, 0.9f, 0.02f), Quaternion.AngleAxis(-hourA, n) * rot);
                m.Box(_steel, fcen + n * 0.03f + (Quaternion.AngleAxis(-minA, n) * Vector3.up) * 0.65f, new Vector3(0.05f, 1.3f, 0.02f), Quaternion.AngleAxis(-minA, n) * rot);
            }
            m.BoxMinMax(_coping, c + new Vector3(-3.1f, 22.5f, -3.1f), c + new Vector3(3.1f, 22.8f, 3.1f));
            // Gallery: four arched openings on slender piers, a dome above.
            for (int s = 0; s < 4; s++)
            {
                var r = Rect.MinMaxRect(c.x - 2.4f, c.z - 2.4f, c.x + 2.4f, c.z + 2.4f);
                var (a0, b0) = FaceRun(r, s);
                m.WallWithArches(_stone, a0 + Vector3.up * (c.y + 22.8f), b0 + Vector3.up * (c.y + 22.8f), 3.4f, SideOut3(s), new List<Vector3> { new Vector3(2.4f, 3.0f, 1.6f) }, true);
            }
            m.BoxMinMax(_stone, c + new Vector3(-2.6f, 26.2f, -2.6f), c + new Vector3(2.6f, 26.6f, 2.6f));
            Dome(m, _stone, c + Vector3.up * 26.6f, 2.3f, 2.6f, true, 24);
            Finial(m, c + Vector3.up * 29.1f, 1.0f);
            m.Build(group, "ClockTower", layer, "Concrete");
            _townSolids.Add(Rect.MinMaxRect(c.x - 4f, c.z - 4f, c.x + 4f, c.z + 4f));
            foreach (var o in new[] { new Vector2(7.5f, 7.5f), new Vector2(-7.5f, 7.5f), new Vector2(7.5f, -7.5f), new Vector2(-7.5f, -7.5f) })
                PlaceTown(props, PackPrefab("Palm_SingleStraight"), new Vector3(c.x + o.x, c.y, c.z + o.y), o.x * 13f, 9f, navBlock: false);
            NoEntryBox(Rect.MinMaxRect(c.x - RoundaboutIsland, c.z - RoundaboutIsland, c.x + RoundaboutIsland, c.z + RoundaboutIsland), c.y - 2f, c.y + 40f, 1.5f);
        }

        // ==================================================================
        // The bazaar gate and the souk
        // ==================================================================
        /// <summary>
        /// The souk's gate on the west side of the roundabout: a stone gatehouse with a great pointed arch, the
        /// "Bab al-Souq" inscription over it, crenellations and corner turrets, and a paved forecourt to the ring.
        /// </summary>
        private static void BuildBazaarGate(Transform group, int layer)
        {
            var m = new TownMesh();
            float gx = SoukRect.xMax;        // the gate's east face, on the forecourt
            float y0 = SampleTownBase(gx, 20f) + 0.18f;
            float bottom = y0 - 1.2f;
            const float W = 16f, D = 5f, H = 13f;
            float z0 = 20f - W * 0.5f, z1 = 20f + W * 0.5f;
            // Forecourt from the gate to the roundabout's pavement.
            m.Quad(_paversPlaza, new Vector3(gx, y0 - 0.02f, z0 - 6f), new Vector3(-RoundaboutWalk + 2f, y0 - 0.02f, z0 - 6f),
                   new Vector3(-RoundaboutWalk + 2f, y0 - 0.02f, z1 + 6f), new Vector3(gx, y0 - 0.02f, z1 + 6f), Vector3.up);
            // The two towers and the arch between.
            m.BoxMinMax(_stoneWarm, new Vector3(gx - D, bottom, z0 - 1f), new Vector3(gx, y0 + H, z0 + 3.5f));
            m.BoxMinMax(_stoneWarm, new Vector3(gx - D, bottom, z1 - 3.5f), new Vector3(gx, y0 + H, z1 + 1f));
            var arch = new List<Vector3> { new Vector3(4.5f, 7f, 6.5f) };
            m.WallWithArches(_stoneWarm, new Vector3(gx, y0, z0 + 3.5f), new Vector3(gx, y0, z1 - 3.5f), H, Vector3.right, arch, true);
            m.WallWithArches(_stoneWarm, new Vector3(gx - D, y0, z0 + 3.5f), new Vector3(gx - D, y0, z1 - 3.5f), H, Vector3.left, arch, true);
            m.ArchSoffit(_stoneWarm, new Vector3(gx, y0, z0 + 3.5f), Vector3.forward, Vector3.left, 4.5f, 7f, 6.5f, D, true);
            m.BoxMinMax(_stoneWarm, new Vector3(gx - D, y0 + H - 0.05f, z0 - 1f), new Vector3(gx, y0 + H + 0.4f, z1 + 1f));
            Merlons(m, _stoneWarm, new Vector2(gx, z0 - 1f), new Vector2(gx, z1 + 1f), y0 + H + 0.4f, 1f, Vector2.right, 0.8f, 1.0f);
            // Inscription panel over the arch.
            var cell = SignCell("sh_soukgate");
            float sx = gx + 0.06f, sh = 1.5f, sw = 6f;
            // Facing east, read from the roundabout: the reader's left is south.
            m.QuadUV(_townSignMat, new Vector3(sx, y0 + 10.6f, 20f - sw * 0.5f), new Vector3(sx, y0 + 10.6f, 20f + sw * 0.5f),
                     new Vector3(sx, y0 + 10.6f + sh, 20f + sw * 0.5f), new Vector3(sx, y0 + 10.6f + sh, 20f - sw * 0.5f), Vector3.right,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            // Turrets.
            foreach (float zt in new[] { z0 - 0.2f, z1 + 0.2f })
            {
                m.Cylinder(_stoneWarm, new Vector3(gx - 0.8f, y0 + H + 0.4f, zt), 0.9f, 2.6f, 14);
                Dome(m, _domeTile, new Vector3(gx - 0.8f, y0 + H + 3f, zt), 1.0f, 1.3f, true, 14);
            }
            m.Build(group, "BazaarGate", layer, "Concrete");
            _townSolids.Add(Rect.MinMaxRect(gx - D, z0 - 1f, gx, z0 + 3.5f));
            _townSolids.Add(Rect.MinMaxRect(gx - D, z1 - 3.5f, gx, z1 + 1f));
            NoEntryBox(Rect.MinMaxRect(gx - D, z0 - 1f, gx, z0 + 3.5f), bottom - 1f, y0 + 7f, 0f);
            NoEntryBox(Rect.MinMaxRect(gx - D, z1 - 3.5f, gx, z1 + 1f), bottom - 1f, y0 + 7f, 0f);
            NoEntryBox(Rect.MinMaxRect(gx - D, z0 - 1f, gx, z1 + 1f), y0 + 6.3f, y0 + 20f, 0f);
        }

        /// <summary>
        /// The souk: a covered street from the gate into the old town, 8 m wide, under a stone barrel vault with
        /// oculi that let the sun down in shafts. Both sides are two-storey arcaded shopfronts, open, their goods out in
        /// front: baskets of produce and spices, brass and pottery, carpets hung on the walls, crates and sacks, a lantern
        /// at every shop. Side passages every fifty metres lead out into the alleys.
        /// </summary>
        private static void BuildSouk(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            float x0 = SoukRect.xMin, x1 = SoukRect.xMax - 5f;          // inside the gate
            const float zs0 = 16f, zs1 = 24f, Shop = 4f, FrontH = 7f, VaultSpring = 7f;
            float y0 = SampleTownBase((x0 + x1) * 0.5f, 20f) + 0.18f;
            // Street floor: stone, following the ground gently.
            foreach (var (a, b) in Pieces(x0, x1 + 5f, 6f))
            {
                float ya = SampleTownBase(a, 20f) + 0.18f, yb = SampleTownBase(b, 20f) + 0.18f;
                m.Quad(_paversPlaza, new Vector3(a, ya, zs0 - Shop), new Vector3(b, yb, zs0 - Shop), new Vector3(b, yb, zs1 + Shop), new Vector3(a, ya, zs1 + Shop), Vector3.up);
            }
            float bottom = y0 - 1.5f;
            var passages = new List<float>();
            for (float x = x0 + 40f; x < x1 - 20f; x += 50f) passages.Add(x);

            // The two shop rows: a front wall with an arch per shop, a back wall, a roof over each row at FrontH.
            foreach (float side in new[] { -1f, 1f })
            {
                float zf = side < 0 ? zs0 : zs1, zb = zf + side * Shop;
                var n = side < 0 ? Vector3.forward : Vector3.back;   // the shopfront faces the street
                var a = new Vector3(x0, 0, zf); var b = new Vector3(x1, 0, zf);
                float len = x1 - x0;
                var arches = new List<Vector3>();
                for (float c = 1.8f; c < len - 1.5f; c += 3.6f)
                {
                    bool passage = false;
                    foreach (var p in passages) if (Mathf.Abs(x0 + c - p) < 1.9f) passage = true;
                    arches.Add(new Vector3(c, passage ? 3.2f : 3.0f, passage ? 2.6f : 2.2f));
                }
                m.WallWithArches(_stoneWarm, a + Vector3.up * bottom, b + Vector3.up * bottom, FrontH + y0 - bottom, n,
                                 arches.ConvertAll(ar => new Vector3(ar.x, ar.y, ar.z + (y0 - bottom))), true, 10);
                // Back wall, roof slab over the shops, a ceiling.
                m.WallWithHoles(_stoneWarm, new Vector3(x0, bottom, zb), new Vector3(x1, bottom, zb), FrontH + y0 - bottom + 0.5f, -n, PassageHoles(passages, x0, y0 - bottom));
                m.BoxMinMax(_stoneWarm, new Vector3(x0, y0 + FrontH, Mathf.Min(zf, zb)), new Vector3(x1, y0 + FrontH + 0.5f, Mathf.Max(zf, zb)));
                // The shops' ceiling sits above the arch heads (4.2 m), or the arches look up into the empty row and the sky.
                m.Quad(_shopDark, new Vector3(x0, y0 + 4.6f, Mathf.Min(zf, zb)), new Vector3(x1, y0 + 4.6f, Mathf.Min(zf, zb)), new Vector3(x1, y0 + 4.6f, Mathf.Max(zf, zb)), new Vector3(x0, y0 + 4.6f, Mathf.Max(zf, zb)), Vector3.down);
                foreach (var ar in arches)
                {
                    m.ArchSoffit(_stoneWarm, a + Vector3.up * y0, Vector3.right, -n, ar.x, ar.y, ar.z, 0.4f, true, 8);
                    bool passage = false;
                    foreach (var p in passages) if (Mathf.Abs(x0 + ar.x - p) < 1.9f) passage = true;
                    float cx = x0 + ar.x;
                    if (passage) continue;
                    // The shop inside: dark walls, its floor; partitions between shops.
                    m.Quad(_shopDark, new Vector3(cx - 1.75f, y0, zb - side * 0.02f), new Vector3(cx + 1.75f, y0, zb - side * 0.02f), new Vector3(cx + 1.75f, y0 + 4.6f, zb - side * 0.02f), new Vector3(cx - 1.75f, y0 + 4.6f, zb - side * 0.02f), n);
                    m.BoxMinMax(_stoneWarm, new Vector3(cx + 1.6f, y0, Mathf.Min(zf, zb)), new Vector3(cx + 2.0f, y0 + 4.6f, Mathf.Max(zf, zb)));
                    DressSoukShop(props, new Vector3(cx, y0, zf), n, side, rng);
                }
                // Lanterns hung under the arch heads, every other shop.
                for (int i = 0; i < arches.Count; i += 2)
                    PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "brass_diya_lantern" : "Lantern_01"), new Vector3(x0 + arches[i].x, y0 + 2.5f, zf + n.z * 0.5f), 0f, 0.4f, navBlock: false);
            }
            // The barrel vault over the street, springing from the shop roofs, with square oculi every eight metres.
            const int seg = 14;
            float rad = (zs1 - zs0) * 0.5f + Shop * 0.15f;
            for (float x = x0; x < x1 - 0.01f; x += 4f)
            {
                float xe = Mathf.Min(x + 4f, x1);
                bool oculus = Mathf.RoundToInt((x - x0) / 4f) % 2 == 1;
                for (int k = 0; k < seg; k++)
                {
                    float a0 = Mathf.PI * k / seg, a1 = Mathf.PI * (k + 1) / seg;
                    if (oculus && k >= seg / 2 - 1 && k <= seg / 2) continue;
                    Vector3 P(float xx, float a) => new Vector3(xx, y0 + VaultSpring + Mathf.Sin(a) * rad * 0.8f, 20f + Mathf.Cos(a) * rad);
                    var nIn = -new Vector3(0f, Mathf.Sin((a0 + a1) * 0.5f), Mathf.Cos((a0 + a1) * 0.5f));
                    m.Quad(_stoneWarm, P(x, a0), P(xe, a0), P(xe, a1), P(x, a1), nIn);
                    m.Quad(_stoneWarm, P(x, a0) - nIn * 0.4f, P(xe, a0) - nIn * 0.4f, P(xe, a1) - nIn * 0.4f, P(x, a1) - nIn * 0.4f, -nIn);
                }
                // Transverse arch rib every 8 m.
                if (!oculus) m.Box(_stone, new Vector3(x, y0 + VaultSpring + rad * 0.4f, 20f), new Vector3(0.5f, 0.3f, zs1 - zs0 + 0.4f), Quaternion.identity);
            }
            // The street's far end: an arch out to the west.
            m.WallWithArches(_stoneWarm, new Vector3(x0, y0, zs0 - Shop), new Vector3(x0, y0, zs1 + Shop), FrontH + 4f, Vector3.left, new List<Vector3> { new Vector3(Shop + 4f, 6f, 4f) }, true);

            m.Build(group, "Souk", layer, "Concrete");
            // The shop rows and the vault: off the bake; the street and the passages stay walkable.
            // The shop rows, cut at the side passages so they stay open to the alleys.
            var cuts = new List<float> { x0 };
            foreach (var p in passages) { cuts.Add(p - 1.4f); cuts.Add(p + 1.4f); }
            cuts.Add(x1);
            for (int i = 0; i + 1 < cuts.Count; i += 2)
            {
                NoEntryBox(Rect.MinMaxRect(cuts[i], zs0 - Shop, cuts[i + 1], zs0), bottom, y0 + 20f, 0.2f);
                NoEntryBox(Rect.MinMaxRect(cuts[i], zs1, cuts[i + 1], zs1 + Shop), bottom, y0 + 20f, 0.2f);
            }
            foreach (var p in passages)
            {
                NoEntryBox(Rect.MinMaxRect(p - 1.4f, zs0 - Shop, p + 1.4f, zs0), y0 + 3.4f, y0 + 20f, 0f);
                NoEntryBox(Rect.MinMaxRect(p - 1.4f, zs1, p + 1.4f, zs1 + Shop), y0 + 3.4f, y0 + 20f, 0f);
            }
            NoEntryBox(Rect.MinMaxRect(x0, zs0, x1, zs1), y0 + VaultSpring - 1f, y0 + 20f, 0f);
            _townSolids.Add(Rect.MinMaxRect(x0, zs0 - Shop, x1, zs0));
            _townSolids.Add(Rect.MinMaxRect(x0, zs1, x1, zs1 + Shop));
            _soukStreet = Rect.MinMaxRect(x0, zs0, x1, zs1);
        }

        /// <summary>The souk's street, for the civilians who shop there.</summary>
        private static Rect _soukStreet;

        private static List<Vector4> PassageHoles(List<float> passages, float x0, float v0)
        {
            var list = new List<Vector4>();
            foreach (var p in passages) list.Add(new Vector4(p - x0 - 1.5f, p - x0 + 1.5f, v0, v0 + 3f));
            return list;
        }

        /// <summary>One shop's goods: what it sells, out on the street in front and on the walls behind.</summary>
        private static void DressSoukShop(Transform props, Vector3 front, Vector3 n, float side, System.Random rng)
        {
            int kind = rng.Next(6);
            Vector3 P(float along, float outFromFront) => front + Vector3.right * along + n * outFromFront;
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            switch (kind)
            {
                case 0: // produce
                    PlaceTown(props, Prop("painted_wooden_table"), P(0f, 0.7f), yaw, 0.85f);
                    foreach (var id in new[] { "bananas", "food_pomegranate_01", "food_lime_01", "yellow_onion", "food_apple_01" })
                        if (rng.NextDouble() < 0.7) PlaceTown(props, Prop("wicker_basket_01"), P(Rand(rng, -1.3f, 1.3f), Rand(rng, 1.2f, 1.6f)), Rand(rng, 0f, 360f), 0.35f);
                    PlaceTown(props, Prop("plastic_crate_01"), P(-1.2f, 0.4f), yaw, 0.32f);
                    PlaceTown(props, Prop("plastic_crate_02"), P(1.2f, 0.4f), yaw, 0.32f);
                    break;
                case 1: // brass and pottery
                    PlaceTown(props, Prop("wooden_display_shelves_01"), P(0f, -1.8f), yaw + 180f, 2.0f);
                    foreach (var id in new[] { "brass_pot_01", "brass_pot_02", "brass_vase_01", "brass_vase_02", "brass_vase_03", "brass_goblets" })
                        if (rng.NextDouble() < 0.7) PlaceTown(props, Prop(id), P(Rand(rng, -1.4f, 1.4f), Rand(rng, 0.2f, 0.9f)), Rand(rng, 0f, 360f));
                    break;
                case 2: // ceramics
                    foreach (var id in new[] { "ceramic_vase_01", "ceramic_vase_02", "ceramic_vase_03", "ceramic_vase_04", "antique_ceramic_vase_01", "ceramic_pot", "jug_01" })
                        if (rng.NextDouble() < 0.75) PlaceTown(props, Prop(id), P(Rand(rng, -1.5f, 1.5f), Rand(rng, 0.1f, 1.0f)), Rand(rng, 0f, 360f));
                    break;
                case 3: // household goods
                    PlaceTown(props, Prop("steel_frame_shelves_01"), P(0f, -2.0f), yaw + 180f, 2.0f);
                    foreach (var id in new[] { "pot_enamel_01", "tea_set_01", "wooden_bowl_01", "carved_wooden_plate", "plastic_bottle_gallon" })
                        if (rng.NextDouble() < 0.7) PlaceTown(props, Prop(id), P(Rand(rng, -1.4f, 1.4f), Rand(rng, 0.2f, 0.8f)), Rand(rng, 0f, 360f));
                    break;
                case 4: // café: a table and plastic chairs out front, a tea set
                    PlaceTown(props, Prop("outdoor_table_chair_set_01"), P(0f, 1.6f), yaw + Rand(rng, -20f, 20f));
                    PlaceTown(props, Prop("plastic_monobloc_chair_01"), P(-1.4f, 0.8f), yaw + Rand(rng, -40f, 40f));
                    PlaceTown(props, Prop("plastic_monobloc_chair_01"), P(1.4f, 0.8f), yaw + 180f + Rand(rng, -40f, 40f));
                    break;
                default: // carpets: one hung on the back wall, rolls in front
                    PlaceTown(props, Prop("wooden_crate_01"), P(Rand(rng, -1f, 1f), 0.5f), Rand(rng, 0f, 360f), 0.6f);
                    PlaceTown(props, Prop("wooden_crate_02"), P(Rand(rng, -1f, 1f), 1.0f), Rand(rng, 0f, 360f), 0.5f);
                    break;
            }
            // A stool for the shopkeeper.
            PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "folding_wooden_stool" : "wooden_stool_01"), P(Rand(rng, -1.2f, 1.2f), -0.8f), Rand(rng, 0f, 360f));
            _shopFronts.Add(new Vector4(front.x + n.x * 1.6f, front.y, front.z + n.z * 1.6f, Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg));
            _ = side;
        }

        // ==================================================================
        /// <summary>A small neighbourhood mosque in the old town's square: a domed prayer room and a slim minaret.</summary>
        private static void BuildNeighbourhoodMosque(Transform group, int layer, System.Random rng)
        {
            if (_oldSquares.Count == 0) return;
            var sq = _oldSquares[0];
            var m = new TownMesh();
            float y0 = FloorLevel(sq);
            var room = Rect.MinMaxRect(sq.xMin + 1f, sq.center.y, sq.xMin + 1f + 11f, sq.center.y + 11f);
            if (room.xMax > sq.xMax - 1f) room.xMax = sq.xMax - 1f;
            for (int s = 0; s < 4; s++)
            {
                var (a0, b0) = FaceRun(room, s);
                var n = SideOut3(s);
                var holes = new List<Vector4>();
                if (s == 1) holes.Add(new Vector4(Vector3.Distance(a0, b0) * 0.5f - 0.7f, Vector3.Distance(a0, b0) * 0.5f + 0.7f, 1.2f, 3.4f));
                else holes.Add(new Vector4(Vector3.Distance(a0, b0) * 0.5f - 0.4f, Vector3.Distance(a0, b0) * 0.5f + 0.4f, 3.0f, 4.3f));
                m.WallWithHoles(_stucco, a0 + Vector3.up * (y0 - 1.2f), b0 + Vector3.up * (y0 - 1.2f), 6.4f, n, holes);
            }
            m.BoxMinMax(_stucco, new Vector3(room.xMin, y0 + 5.2f, room.yMin), new Vector3(room.xMax, y0 + 5.5f, room.yMax));
            Dome(m, _stucco, new Vector3(room.center.x, y0 + 5.5f, room.center.y), 4.6f, 4.0f, false, 28);
            Finial(m, new Vector3(room.center.x, y0 + 9.5f, room.center.y), 0.8f);
            var mc = new Vector3(room.xMax + 2.2f, y0, room.yMax - 1.8f);
            m.Cylinder(_stucco, mc + Vector3.down * 1.2f, 1.2f, 18f, 16, true);
            m.Cylinder(_coping, mc + Vector3.up * 14f, 1.8f, 0.3f, 16, true, true);
            m.Cylinder(_stucco, mc + Vector3.up * 16.8f, 0.9f, 2f, 12, true);
            Dome(m, _domeTile, mc + Vector3.up * 18.8f, 1.0f, 1.6f, true, 14);
            Finial(m, mc + Vector3.up * 20.3f, 0.6f);
            m.Build(group, "NeighbourhoodMosque", layer, "Concrete");
            NoEntryBox(room, y0 - 2f, y0 + 12f, 0.3f);
            _townSolids.Add(room);
            _townSolids.Add(Rect.MinMaxRect(mc.x - 1.3f, mc.z - 1.3f, mc.x + 1.3f, mc.z + 1.3f));
            _ = rng;
        }
    }
}
#endif
