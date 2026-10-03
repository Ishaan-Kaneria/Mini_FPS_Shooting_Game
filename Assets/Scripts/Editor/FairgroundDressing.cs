#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Step 3 of the Abandoned Fairground rebuild: the story of a fair that flooded, shut down and was looted.
    /// Placed on top of the Step 2 scene by <see cref="FairgroundDress"/>. Everything is a primitive or a pack prop
    /// carrying our own generated textures (fairground signs, tape, graffiti, litter); no real brands.
    ///   signs (some broken, hanging or fallen) - quarantine / caution tape - plywood boarding - sandbag emplacements
    ///   torn tarps (they sway, WindFlutter) - toppled bins - camps with sleeping bags and fire remains
    ///   burning-barrel anchors for the lighting pass - mostly dead string lights (a few can flicker)
    ///   scattered tickets and cups, spray-painted warnings.
    /// </summary>
    public static class FairgroundDressing
    {
        const string SignDir = "Assets/Fairground/Signs";
        const string DecDir = "Assets/Fairground/Decals";
        const string MatDir = "Assets/Fairground/Materials/Dressed";

        static System.Random rng;
        static Transform root, g;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> PackCache = new Dictionary<string, GameObject>();
        static int signs, tapes, boards, bags, tarps, bins, camps, bulbs, liveBulbs, wires, litter, graffiti, barrels, clutter;

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float H(float x, float z) => FairgroundBlockout.H(x, z);

        // ------------------------------------------------------------------ materials

        static Material Lit(string name, Color tint, Texture2D map = null, Texture2D bump = null, float smooth = 0.2f, Color? emission = null)
        {
            if (Mats.TryGetValue(name, out var cached)) return cached;
            FairgroundMaterialConverter.EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", map);
            if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Texture2D T(string dir, string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}.png");
        static Material Sign(string tex) => Lit("S_" + tex, Color.white, T(SignDir, tex), null, 0.25f);
        static Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        /// <summary>Torn tarp: a generated texture with holes and frayed edges, alpha-clipped and two-sided.</summary>
        static Material TornTarp(string tex)
        {
            if (Mats.TryGetValue("TT_" + tex, out var cached)) return cached;
            var m = Lit("TT_" + tex, Color.white, T(SignDir, tex), null, 0.15f);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Fabric(string name, Color tint)
        {
            const string tp = "Assets/Fairground/ThirdParty/Fabric061";
            return Lit(name, tint, T(tp, "Fabric061_2K-JPG_Color"), T(tp, "Fabric061_2K-JPG_NormalGL"), 0.12f);
        }

        // ------------------------------------------------------------------ primitives

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        static GameObject Slab(string name, Transform parent, Vector3 pos, float yaw, float w, float h, float thick, Material m, float roll = 0f, float pitch = 0f)
            => Prim(PrimitiveType.Cube, name, parent, pos, Quaternion.Euler(pitch, yaw, roll), new Vector3(w, h, thick), m, false);

        static Vector3 P(float x, float z, float up = 0f) => new Vector3(x, H(x, z) + up, z);

        static GameObject Pack(string name)
        {
            if (PackCache.TryGetValue(name, out var gobj)) return gobj;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Fairground/ThirdParty/FloodedGrounds/Prefabs" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) { gobj = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
            PackCache[name] = gobj;
            return gobj;
        }

        static GameObject Spawn(string prefab, Vector3 pos, float yaw, float pitch = 0f, float roll = 0f, float scale = 1f)
        {
            var src = Pack(prefab);
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            FairgroundMaterialConverter.Remap(go);
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, roll));
            go.transform.localScale = Vector3.one * scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ entry

        public static string Place(Transform sceneRoot, List<(Vector2 a, Vector2 b, float w)> strips, int seed = 31)
        {
            rng = new System.Random(seed);
            root = sceneRoot;
            Mats.Clear(); PackCache.Clear();
            signs = tapes = boards = bags = tarps = bins = camps = bulbs = liveBulbs = wires = litter = graffiti = barrels = clutter = 0;
            g = new GameObject("13_Dressing").transform;
            g.SetParent(root, false);

            Signs();
            Tapes();
            Boarding();
            Sandbags();
            Tarps();
            Bins(strips);
            Camps();
            Barrels();
            StringLights();
            Clutter();
            LitterAndGraffiti(strips);
            return $"dressing: {signs} signs, {tapes} tape strands, {boards} boards, {bags} sandbags, {tarps} tarps, {bins} bins, {camps} camps, " +
                   $"{barrels} barrels, {wires} light wires ({bulbs} bulbs, {liveBulbs} live), {clutter} furniture pieces, {litter} litter + {graffiti} graffiti decals";
        }

        // ------------------------------------------------------------------ 1 signs

        static void StandSign(string tex, float x, float z, float yaw, float w, float h, float postH, float roll = 0f)
        {
            Vector3 p = P(x, z);
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var post = Existing("RustDark");
            foreach (float s in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "SignPost", g, p + right * s * (w * 0.5f - 0.1f) + Vector3.up * postH * 0.5f, Quaternion.Euler(0, yaw, 0), new Vector3(0.12f, postH, 0.12f), post);
            Slab("Sign", g, p + Vector3.up * (postH - h * 0.5f - 0.1f) + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.1f, yaw, w, h, 0.05f, Sign(tex), roll);
            signs++;
        }

        static void Signs()
        {
            // The arch: a banner with letters missing, on the face the visitor arrives from (north).
            float gy = H(0, 82);
            Slab("ArchBanner", g, new Vector3(0, gy + 9.2f, 82.77f), 0, 20f, 2.0f, 0.06f, Sign("Sign_ArchBanner")); signs++;

            // Ticket booths and stalls.
            var gate = root.Find("1_MainGate");
            if (gate != null)
                foreach (Transform t in gate)
                    if (t.name == "TicketBooth") { Slab("Sign", g, t.position + new Vector3(0, 3.55f, 1.65f), 0, 2.6f, 0.7f, 0.05f, Sign("Sign_Tickets"), R(-4f, 4f), -8f); signs++; }
            var mid = root.Find("2_Midway");
            string[] stallSigns = { "Sign_Prizes", "Sign_HotDogs", "Sign_Games", "Sign_Tickets", "Sign_Games", "Sign_HotDogs" };
            if (mid != null)
                foreach (Transform t in mid)
                {
                    if (t.name != "Stall") continue;
                    if (rng.NextDouble() < 0.2) continue;                                       // the sign is gone
                    bool crooked = rng.NextDouble() < 0.45;
                    Vector3 pos = t.TransformPoint(new Vector3(0, 2.75f, 2.65f));
                    var s = Slab("Sign", g, pos, t.eulerAngles.y, 3.0f, 0.75f, 0.05f, Sign(stallSigns[rng.Next(stallSigns.Length)]), crooked ? R(-14f, 14f) : 0f, 4f);
                    if (crooked) Flutter(s, 3f);
                    signs++;
                }

            // Rides and routes.
            StandSign("Sign_FerrisWheel", 41f, -8f, 270f, 3.4f, 1.7f, 3.2f);
            StandSign("Sign_Carousel", -9f, 6f, 0f, 3.2f, 1.6f, 3.0f, 3f);
            StandSign("Sign_Coaster", -48f, 19f, 90f, 3.4f, 1.7f, 3.2f);
            StandSign("Sign_BigTop", -19f, -46.5f, 0f, 6f, 2.4f, 4.4f);      // beside the north mouth, not across it
            Slab("Sign", g, new Vector3(62f, 4.2f, -46.9f), 0, 6f, 1.8f, 0.06f, Sign("Sign_BumperCars")); signs++;
            StandSign("Sign_StaffOnly", 45.4f, 25.5f, 270f, 2.2f, 1.1f, 2.4f);
            StandSign("Sign_StaffOnly", 58.5f, 11.6f, 180f, 2.2f, 1.1f, 2.4f);
            StandSign("Sign_Exit", -30f, 61.4f, 180f, 1.8f, 0.9f, 2.6f, -6f);
            StandSign("Sign_DoNotEnter", 11.5f, -64f, 90f, 1.6f, 1.6f, 2.0f);
            StandSign("Sign_DoNotEnter", -31.5f, -72f, 270f, 1.6f, 1.6f, 2.0f);
            foreach (var p in new[] { new Vector2(36.6f, -11f), new Vector2(-42f, 26.5f), new Vector2(3.5f, -2f), new Vector2(30f, -38.5f), new Vector2(76f, -36f), new Vector2(57f, -45f) })
                StandSign("Sign_RideClosed", p.x, p.y, R(0, 360), 1.5f, 1.1f, 1.7f, R(-5f, 5f));

            // Fallen signs, leaning where they dropped.
            string[] all = { "Sign_Prizes", "Sign_HotDogs", "Sign_Games", "Sign_Tickets", "Sign_RideClosed", "Sign_Carousel", "Sign_FerrisWheel" };
            for (int i = 0; i < 9; i++)
            {
                float x = R(-40f, 40f), z = R(-6f, 52f);
                Slab("FallenSign", g, P(x, z, 0.1f), R(0, 360), 2.6f, 0.75f, 0.05f, Sign(all[rng.Next(all.Length)]), R(-10f, 10f), R(70f, 86f)); signs++;
            }
        }

        // ------------------------------------------------------------------ 2 tape

        static void Tape(Vector3 a, Vector3 b, string tex, float sag = 0.25f, bool brokenAtB = false)
        {
            var m = Lit("T_" + tex, Color.white, T(SignDir, tex), null, 0.3f);
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 2.5f));
            Vector3 prev = a;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                if (brokenAtB && t > 0.55f) p.y = Mathf.Lerp(p.y, H(p.x, p.z) + 0.03f, Mathf.InverseLerp(0.55f, 0.85f, t));   // one end dropped to the ground
                Vector3 d = p - prev;
                if (d.sqrMagnitude > 1e-4f)
                {
                    var s = Prim(PrimitiveType.Cube, "Tape", g, (prev + p) * 0.5f, Quaternion.LookRotation(d, Vector3.up), new Vector3(0.004f, 0.07f, d.magnitude + 0.01f), m, false);
                    if (rng.NextDouble() < 0.3 && !brokenAtB) Flutter(s, 6f, 0.5f, Vector3.forward);
                }
                prev = p;
            }
            tapes++;
        }

        static void TapePosts(Vector3 a, Vector3 b)
        {
            var post = Existing("RustDark");
            foreach (var p in new[] { a, b })
                Prim(PrimitiveType.Cylinder, "TapePost", g, new Vector3(p.x, H(p.x, p.z) + 0.65f, p.z), Quaternion.identity, new Vector3(0.07f, 0.65f, 0.07f), post);
        }

        static void Line(float x0, float z0, float x1, float z1, string tex, float h = 1.1f, float sag = 0.22f, bool posts = true, bool broken = false)
        {
            Vector3 a = P(x0, z0, h), b = P(x1, z1, h);
            if (posts) TapePosts(a, b);
            Tape(a, b, tex, sag, broken);
        }

        static void Tapes()
        {
            // Big Top: the east and west mouths taped shut, the north one torn.
            Line(9f, -64.5f, 9f, -71.5f, "Tape_Quarantine", 1.0f); Line(9f, -64.5f, 9f, -71.5f, "Tape_DoNotCross", 1.35f, 0.2f, false);
            Line(-29f, -64.5f, -29f, -71.5f, "Tape_Caution", 1.1f); Line(-29f, -64.5f, -29f, -71.5f, "Tape_Quarantine", 1.45f, 0.2f, false, true);
            Line(-13.8f, -49.5f, -6f, -49.5f, "Tape_DoNotCross", 1.2f, 0.3f, true, true);
            // Rides.
            Line(36.8f, -12.4f, 36.8f, -15.6f, "Tape_Caution", 1.1f);
            Line(-41.6f, 22.4f, -41.6f, 25.6f, "Tape_DoNotCross", 1.1f);
            Line(-52.4f, 22.4f, -52.4f, 25.6f, "Tape_Quarantine", 1.1f, 0.2f, true, true);
            // Carousel: a ring of posts with two gaps where the tape is down.
            for (int i = 0; i < 9; i++)
            {
                float a0 = i / 9f * 6.283f + 0.2f, a1 = (i + 1) / 9f * 6.283f + 0.2f;
                var c = FairgroundBlockout.CarouselCentre;
                if (i == 2 || i == 6) { TapePosts(P(c.x + Mathf.Cos(a0) * 9.4f, c.y + Mathf.Sin(a0) * 9.4f, 1.1f), P(c.x + Mathf.Cos(a0) * 9.4f, c.y + Mathf.Sin(a0) * 9.4f, 1.1f)); continue; }
                Line(c.x + Mathf.Cos(a0) * 9.4f, c.y + Mathf.Sin(a0) * 9.4f, c.x + Mathf.Cos(a1) * 9.4f, c.y + Mathf.Sin(a1) * 9.4f, i % 2 == 0 ? "Tape_Caution" : "Tape_DoNotCross", 1.1f, 0.18f, true, i == 4);
            }
            // The chained gate and the Midway mouth.
            Line(-3.6f, 62.6f, 3.6f, 63.4f, "Tape_DoNotCross", 1.3f, 0.3f, false);
            Line(-3.6f, 63.4f, 3.6f, 62.6f, "Tape_Quarantine", 1.0f, 0.3f, false, true);
            Line(-12f, 54f, -4f, 54f, "Tape_Caution", 1.15f, 0.25f, true, true);
            // Backlot gates, garage, kiddie rides, pavilion.
            Line(46f, 27.2f, 46f, 32.8f, "Tape_Caution", 1.1f); Line(60.2f, 12f, 65.8f, 12f, "Tape_DoNotCross", 1.1f);
            Line(76f, 40.2f, 80f, 40.2f, "Tape_Quarantine", 1.3f);
            for (int i = 0; i < 6; i++)
            {
                float a0 = i / 6f * 6.283f, a1 = (i + 1) / 6f * 6.283f;
                if (i == 3) continue;
                Line(30f + Mathf.Cos(a0) * 5.2f, -44f + Mathf.Sin(a0) * 5.2f, 30f + Mathf.Cos(a1) * 5.2f, -44f + Mathf.Sin(a1) * 5.2f, "Tape_Caution", 1.0f, 0.2f, true);
            }
            Line(60f, -48.2f, 64f, -48.2f, "Tape_DoNotCross", 1.2f); Line(53f, -67.8f, 57f, -67.8f, "Tape_Quarantine", 1.2f); Line(67f, -67.8f, 70f, -67.8f, "Tape_Caution", 1.2f, 0.25f, true, true);
        }

        // ------------------------------------------------------------------ 3 boarding

        static void Boards(Vector3 centre, float yaw, float width, float height, int count)
        {
            var wood = Existing("Planks");
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right, fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                float y = Mathf.Lerp(0.25f, height - 0.25f, count == 1 ? 0.5f : i / (float)(count - 1));
                Slab("Plywood", g, centre + Vector3.up * y + fwd * 0.04f + right * R(-0.12f, 0.12f), yaw, width * R(0.82f, 1.02f), 0.26f, 0.05f, wood, R(-9f, 9f)); boards++;
            }
            // two nailed crosspieces
            foreach (float s in new[] { -1f, 1f })
                Slab("Plywood", g, centre + Vector3.up * height * 0.5f + fwd * 0.08f + right * s * width * 0.3f, yaw, 0.2f, height * 0.9f, 0.05f, wood, R(-5f, 5f)); boards++;
        }

        static void Boarding()
        {
            var gate = root.Find("1_MainGate");
            if (gate != null)
                foreach (Transform t in gate)
                    if (t.name == "TicketBooth") Boards(t.position + new Vector3(0, 0, 1.52f) - new Vector3(0, 0.0f, 0) + Vector3.down * 0f, 0, 2.7f, 2.6f, 3);
            Boards(P(0, 26 + 2.55f), 0, 6.6f, 2.6f, 3);                    // the Midway kiosk, north face
            Boards(P(0, 26 - 2.55f), 180, 6.6f, 2.6f, 2);                   // and south
            Boards(P(40.5f, -26f + 1.52f), 0, 2.7f, 2.6f, 3);               // the Ferris ticket booth
            Boards(P(78f, 40.3f), 0, 4.0f, 2.4f, 4);                        // the garage doorway
        }

        // ------------------------------------------------------------------ 4 sandbags

        static void Sandbags()
        {
            var bag = Fabric("SandbagCloth", new Color(0.36f, 0.32f, 0.23f));
            System.Action<Vector2, float, float, int> wall = (c, yaw, length, courses) =>
            {
                Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                for (int row = 0; row < courses; row++)
                {
                    int n = Mathf.CeilToInt(length / 0.58f);
                    for (int i = 0; i < n; i++)
                    {
                        if (row >= 2 && rng.NextDouble() < 0.12) continue;                      // a slumped, missing bag
                        float off = (i - n * 0.5f) * 0.58f + (row % 2 == 0 ? 0f : 0.29f);
                        Vector3 p = P(c.x, c.y) + right * off + Vector3.up * (0.12f + row * 0.23f);
                        Prim(PrimitiveType.Cube, "Sandbag", g, p, Quaternion.Euler(R(-7f, 7f), yaw + R(-12f, 12f), R(-7f, 7f)), new Vector3(0.56f * R(0.88f, 1.12f), 0.22f * R(0.85f, 1.2f), 0.34f * R(0.9f, 1.15f)), bag, false).isStatic = true;
                        bags++;
                    }
                }
                // one solid collider for the whole run (the bags themselves are visual)
                var col = new GameObject("SandbagCollider");
                col.transform.SetParent(g, false);
                col.transform.SetPositionAndRotation(P(c.x, c.y) + Vector3.up * courses * 0.115f, Quaternion.Euler(0, yaw, 0));
                var bc = col.AddComponent<BoxCollider>(); bc.size = new Vector3(length, courses * 0.23f, 0.4f);
                col.isStatic = true;
            };
            wall(new Vector2(-8f, 41f), 0f, 5f, 4); wall(new Vector2(8f, 41f), 0f, 5f, 4);          // Midway, north
            wall(new Vector2(-20f, -3f), 90f, 6f, 4); wall(new Vector2(22f, -3f), 90f, 6f, 4);      // plaza flanks
            wall(new Vector2(-16.5f, -51f), 0f, 5f, 5); wall(new Vector2(-3.5f, -51f), 0f, 5f, 5);  // Big Top north mouth
            wall(new Vector2(43f, 22f), 90f, 5f, 4); wall(new Vector2(34f, -6f), 0f, 5f, 4);        // backlot gate, east lane
            wall(new Vector2(-24f, 66f), 0f, 6f, 4); wall(new Vector2(24f, 66f), 0f, 6f, 4);        // behind the gate fence
        }

        // ------------------------------------------------------------------ 5 tarps

        static void Tarps()
        {
            string[] tex = { "Tarp_Blue", "Tarp_Green", "Tarp_Orange", "Tarp_Grey" };
            var mid = root.Find("2_Midway");
            int k = 0;
            if (mid != null)
                foreach (Transform t in mid)
                {
                    if (t.name != "Stall" || rng.NextDouble() < 0.4) continue;
                    var m = TornTarp(tex[k++ % tex.Length]);
                    Vector3 pos = t.TransformPoint(new Vector3(R(-0.8f, 0.8f), 3.0f, 2.9f));
                    var tarp = Prim(PrimitiveType.Cube, "TornTarp", g, pos, t.rotation * Quaternion.Euler(R(28f, 50f), 0, R(-8f, 8f)), new Vector3(R(2.4f, 3.6f), 0.012f, R(1.6f, 2.4f)), m, false);
                    Flutter(tarp, R(3f, 7f), R(0.25f, 0.5f));
                    tarps++;
                }
            // trailers and the garage
            var bl = root.Find("7_BacklotServiceYard");
            if (bl != null)
                foreach (Transform t in bl)
                    if (t.name == "Trailer")
                    {
                        var tarp = Prim(PrimitiveType.Cube, "TornTarp", g, t.TransformPoint(new Vector3(0, 3.35f, 0)), t.rotation * Quaternion.Euler(R(-6f, 6f), 0, R(-4f, 4f)), new Vector3(3.8f, 0.012f, 5.6f), TornTarp("Tarp_Blue"), false);
                        Flutter(tarp, 2.5f); tarps++;
                    }
        }

        // ------------------------------------------------------------------ 6 bins

        static void Bins(List<(Vector2 a, Vector2 b, float w)> strips)
        {
            var bin = Lit("BinPlastic", new Color(0.18f, 0.32f, 0.20f), null, null, 0.45f);
            for (int i = 0; i < 18; i++)
            {
                var s = strips[rng.Next(strips.Count)];
                Vector2 d = (s.b - s.a).normalized, l = new Vector2(-d.y, d.x);
                Vector2 p = s.a + (s.b - s.a) * R(0.05f, 0.95f) + l * R(-s.w * 0.45f, s.w * 0.45f);
                bool toppled = rng.NextDouble() < 0.65;
                Vector3 pos = P(p.x, p.y, toppled ? 0.38f : 0.45f);
                float yaw = R(0, 360);
                var body = Prim(PrimitiveType.Cylinder, "Bin", g, pos, Quaternion.Euler(toppled ? R(80f, 100f) : 0f, yaw, 0f), new Vector3(0.7f, 0.45f, 0.7f), bin);
                if (toppled)
                {
                    Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                    Prim(PrimitiveType.Cylinder, "BinLid", g, pos + fwd * R(0.9f, 1.8f) + Vector3.down * 0.3f, Quaternion.Euler(R(0f, 12f), yaw, 0f), new Vector3(0.74f, 0.03f, 0.74f), bin, false);
                    for (int k = 0; k < 3; k++) Prim(PrimitiveType.Cube, "Spill", g, pos + fwd * R(-1.8f, -0.6f) + Quaternion.Euler(0, yaw, 0) * Vector3.right * R(-0.6f, 0.6f) + Vector3.down * 0.34f, Quaternion.Euler(0, R(0, 360), 0), new Vector3(R(0.12f, 0.35f), 0.04f, R(0.12f, 0.35f)), Lit("Rubbish", new Color(0.30f, 0.28f, 0.24f)), false);
                }
                bins++;
            }
        }

        // ------------------------------------------------------------------ 7 camps

        static void Camp(Vector2 c, string name)
        {
            var ash = Lit("Ash", new Color(0.05f, 0.05f, 0.055f), null, null, 0.1f);
            var charred = Lit("Charred", new Color(0.04f, 0.035f, 0.03f), null, null, 0.2f);
            float gy = H(c.x, c.y);
            for (int i = 0; i < 9; i++)                                                          // ring of stones
            {
                float a = i / 9f * 6.283f;
                Spawn(Pick("CobbleRock_A", "CobbleRock_C", "CobbleRock_D", "CobbleRock_E"), new Vector3(c.x + Mathf.Cos(a) * 0.66f, gy - 0.02f, c.y + Mathf.Sin(a) * 0.66f), R(0, 360), 0, 0, R(0.22f, 0.32f));
            }
            Prim(PrimitiveType.Cylinder, "CampAsh", g, new Vector3(c.x, gy + 0.015f, c.y), Quaternion.identity, new Vector3(1.0f, 0.012f, 1.0f), ash, false);
            for (int i = 0; i < 4; i++) Prim(PrimitiveType.Cylinder, "CharredLog", g, new Vector3(c.x + R(-0.25f, 0.25f), gy + 0.1f, c.y + R(-0.25f, 0.25f)), Quaternion.Euler(R(75f, 90f), R(0, 180), R(0, 90)), new Vector3(0.1f, 0.38f, 0.1f), charred, false);
            var bagCols = new[] { new Color(0.50f, 0.58f, 0.40f), new Color(0.40f, 0.48f, 0.72f), new Color(0.80f, 0.44f, 0.30f) };
            int nb = 2 + rng.Next(2);
            for (int i = 0; i < nb; i++)
            {
                float a = 0.9f + i * 2.1f + R(-0.3f, 0.3f); float r = R(2.0f, 2.8f);
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, 0, c.y + Mathf.Sin(a) * r); p.y = H(p.x, p.z) + 0.28f;
                float yaw = -a * Mathf.Rad2Deg + 90f + R(-20f, 20f);
                Prim(PrimitiveType.Capsule, "SleepingBag", g, p, Quaternion.Euler(0, yaw, 90f), new Vector3(0.62f, 0.95f, 0.5f), Fabric("Bag" + i, bagCols[i % 3]));
                Prim(PrimitiveType.Sphere, "Pillow", g, p + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.95f + Vector3.up * 0.04f, Quaternion.identity, new Vector3(0.38f, 0.16f, 0.3f), Fabric("Pillow", new Color(0.55f, 0.5f, 0.42f)), false);
                Prim(PrimitiveType.Cube, "Backpack", g, p + Quaternion.Euler(0, yaw, 0) * Vector3.right * 0.9f + Vector3.up * 0.0f, Quaternion.Euler(0, yaw + R(-30f, 30f), 0), new Vector3(0.4f, 0.55f, 0.28f), Fabric("Pack", new Color(0.36f, 0.42f, 0.34f)), false);
            }
            Prim(PrimitiveType.Cylinder, "CookPot", g, new Vector3(c.x + 0.9f, gy + 0.12f, c.y - 0.4f), Quaternion.identity, new Vector3(0.28f, 0.12f, 0.28f), Existing("RustDark"), false);
            for (int i = 0; i < 6; i++) Prim(PrimitiveType.Cylinder, "TinCan", g, new Vector3(c.x + R(-2.2f, 2.2f), gy + 0.05f, c.y + R(-2.2f, 2.2f)), Quaternion.Euler(R(0, 90), R(0, 360), 0), new Vector3(0.08f, 0.05f, 0.08f), Existing("Rust"), false);
            // a lean-to of tarp against the nearest thing, on a pole
            Vector3 tp = new Vector3(c.x - 2.6f, gy + 1.0f, c.y + 0.4f);
            Prim(PrimitiveType.Cylinder, "TarpPole", g, tp + Vector3.down * 0.0f, Quaternion.identity, new Vector3(0.06f, 1.0f, 0.06f), Existing("RustDark"));
            var lt = Prim(PrimitiveType.Cube, "CampTarp", g, tp + new Vector3(0.9f, -0.45f, 0), Quaternion.Euler(0, 0, -34f), new Vector3(2.2f, 0.012f, 2.6f), TornTarp("Tarp_Green"), false);
            Flutter(lt, 3f);
            var anchor = new GameObject("FX_CampFire");                                          // Step 4 puts a low flame here
            anchor.transform.SetParent(g, false);
            anchor.transform.position = new Vector3(c.x, gy + 0.3f, c.y);
            camps++;
        }

        static string Pick(params string[] n) => n[rng.Next(n.Length)];

        /// <summary>Adds WindFlutter. Anything that moves at runtime must not be static (static batching would freeze it).</summary>
        static void Flutter(GameObject go, float amplitude, float frequency = 0.35f, Vector3? axis = null)
        {
            go.isStatic = false;
            var w = go.AddComponent<WindFlutter>();
            w.amplitude = amplitude; w.frequency = frequency;
            if (axis.HasValue) w.axis = axis.Value;
        }

        static void Camps()
        {
            Camp(new Vector2(-57f, 12f), "coaster");              // someone has been living under the coaster
            Camp(new Vector2(62f, 24f), "backlot");               // beside the trailers
            Camp(new Vector2(-22f, -61f), "bigtop");              // in the lee of the bleachers
            Camp(new Vector2(-70f, 74f), "parking");              // among the cars
        }

        // ------------------------------------------------------------------ 8 burning barrels (anchors for the lighting pass)

        static void Barrels()
        {
            var iron = Existing("RustDark");
            var ember = Lit("Ember", new Color(0.12f, 0.04f, 0.02f), null, null, 0.1f, new Color(2.4f, 0.8f, 0.2f));
            foreach (var p in new[] { new Vector2(2.5f, 51f), new Vector2(-6f, -2f), new Vector2(40f, -22f), new Vector2(-46f, 30f), new Vector2(-10f, -52f), new Vector2(66f, 38f) })
            {
                float gy = H(p.x, p.y);
                Prim(PrimitiveType.Cylinder, "BurningBarrel", g, new Vector3(p.x, gy + 0.46f, p.y), Quaternion.identity, new Vector3(0.62f, 0.46f, 0.62f), iron);
                Prim(PrimitiveType.Cylinder, "BarrelEmbers", g, new Vector3(p.x, gy + 0.88f, p.y), Quaternion.identity, new Vector3(0.52f, 0.015f, 0.52f), ember, false);
                var a = new GameObject("FX_BarrelFire");
                a.transform.SetParent(g, false);
                a.transform.position = new Vector3(p.x, gy + 1.05f, p.y);
                barrels++;
            }
        }

        // ------------------------------------------------------------------ 9 string lights

        static float catenary(float t, float sag) => -sag * 4f * t * (1f - t);

        static void Wire(Vector3 a, Vector3 b, float sag, bool snapped, Material wire, Material dead, Material live)
        {
            const int pts = 14;
            var line = new GameObject("StringLightWire");
            line.transform.SetParent(g, false);
            var lr = line.AddComponent<LineRenderer>();
            lr.positionCount = pts; lr.widthMultiplier = 0.03f; lr.sharedMaterial = wire; lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.numCapVertices = 2;
            var positions = new Vector3[pts];
            for (int i = 0; i < pts; i++)
            {
                float t = i / (float)(pts - 1);
                Vector3 p = Vector3.Lerp(a, b, t); p.y += catenary(t, sag);
                if (snapped && t > 0.5f)                                                       // snapped: the far end has fallen to the ground
                {
                    float f = Mathf.InverseLerp(0.5f, 0.78f, t);
                    p.y = Mathf.Lerp(p.y, H(p.x, p.z) + 0.04f, f);
                }
                positions[i] = p;
            }
            lr.SetPositions(positions);
            wires++;
            float total = 0; for (int i = 1; i < pts; i++) total += Vector3.Distance(positions[i - 1], positions[i]);
            int nb = Mathf.Max(2, Mathf.RoundToInt(total / 1.1f));
            for (int k = 1; k < nb; k++)
            {
                float f = k / (float)nb * (pts - 1); int i0 = Mathf.Min(pts - 2, (int)f);
                Vector3 p = Vector3.Lerp(positions[i0], positions[i0 + 1], f - i0);
                bool isLive = rng.NextDouble() < 0.07;
                var bulb = Prim(PrimitiveType.Sphere, isLive ? "BulbLive" : "BulbDead", g, p + Vector3.down * 0.07f, Quaternion.identity, Vector3.one * 0.15f, isLive ? live : dead, false);
                bulbs++; if (isLive) liveBulbs++;
            }
        }

        static void Pole(Vector3 p, float h)
        {
            Prim(PrimitiveType.Cylinder, "LightPole", g, p + Vector3.up * h * 0.5f, Quaternion.Euler(R(-1.5f, 1.5f), 0, R(-1.5f, 1.5f)), new Vector3(0.18f, h * 0.5f, 0.18f), Existing("RustDark"));
        }

        static void StringLights()
        {
            var wire = Lit("WireBlack", new Color(0.02f, 0.02f, 0.02f), null, null, 0.1f);
            wire.shader = Shader.Find("Universal Render Pipeline/Unlit");
            wire.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f));
            var dead = Lit("BulbDead", new Color(0.30f, 0.30f, 0.28f), null, null, 0.9f);
            var live = Lit("BulbLive", new Color(0.4f, 0.28f, 0.14f), null, null, 0.6f, new Color(3.0f, 1.5f, 0.5f));
            const float ph = 5.4f;

            // The Midway: poles down both sides, wires along each side and across the avenue.
            float[] zs = { 50f, 41f, 23f, 14f };
            foreach (float z in zs) { Pole(P(-11.2f, z), ph); Pole(P(11.2f, z), ph); }
            for (int i = 0; i < zs.Length; i++)
            {
                Wire(P(-11.2f, zs[i], ph), P(11.2f, zs[i], ph), 1.5f, rng.NextDouble() < 0.25, wire, dead, live);
                if (i < zs.Length - 1)
                    foreach (float sx in new[] { -11.2f, 11.2f })
                        Wire(P(sx, zs[i], ph), P(sx, zs[i + 1], ph), 0.8f, rng.NextDouble() < 0.2, wire, dead, live);
            }
            // The carousel plaza: a ring of poles and a few spokes to the canopy column.
            var c = FairgroundBlockout.CarouselCentre;
            var ring = new List<Vector3>();
            for (int i = 0; i < 10; i++)
            {
                float a = (i + 0.5f) / 10f * 6.283f;
                if (Mathf.Abs(Mathf.Cos(a)) < 0.25f && Mathf.Sin(a) > 0f) { ring.Add(Vector3.zero); continue; }     // the Midway mouth
                var p = P(c.x + Mathf.Cos(a) * 17.5f, c.y + Mathf.Sin(a) * 17.5f, ph);
                Pole(P(p.x, p.z), ph); ring.Add(p);
            }
            for (int i = 0; i < 10; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % 10];
                if (a == Vector3.zero || b == Vector3.zero) continue;
                Wire(a, b, 0.9f, rng.NextDouble() < 0.25, wire, dead, live);
            }
            for (int i = 0; i < 10; i += 3) if (ring[i] != Vector3.zero) Wire(ring[i], P(c.x, c.y, 5.6f), 1.8f, i == 6, wire, dead, live);
            // The gate plaza and the lowlands boardwalk (lights in the water).
            foreach (float x in new[] { -30f, -15f, 15f, 30f }) Pole(P(x, 60f), ph);
            Wire(P(-30f, 60f, ph), P(-15f, 60f, ph), 0.8f, false, wire, dead, live); Wire(P(-15f, 60f, ph), P(15f, 60f, ph), 1.8f, true, wire, dead, live); Wire(P(15f, 60f, ph), P(30f, 60f, ph), 0.8f, false, wire, dead, live);
            foreach (float z in new[] { -30f, -42f, -54f, -66f }) Pole(new Vector3(34.5f, H(34.5f, z), z), 4.8f);
            for (int i = 0; i < 3; i++)
            {
                float z0 = -30f - i * 12f, z1 = z0 - 12f;
                Wire(new Vector3(34.5f, 4.8f, z0), new Vector3(34.5f, 4.8f, z1), 1.0f, true, wire, dead, live);
            }
        }

        // ------------------------------------------------------------------ 10 furniture clusters (someone ate here once)

        static void Clutter()
        {
            for (int i = 0; i < 9; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * R(8.5f, 10.5f), z = R(9f, 52f);
                if (z > 28f && z < 35f) continue;
                var c = P(x, z);
                Spawn("Prop_SmallTable_A", c, R(0, 360)); clutter++;
                for (int k = 0; k < 3; k++)
                {
                    bool over = rng.NextDouble() < 0.5;
                    Spawn(Pick("Prop_Chair_A", "Prop_Chair_B"), c + new Vector3(R(-1.1f, 1.1f), 0.0f, R(-1.1f, 1.1f)), R(0, 360), over ? 90f : 0f, over ? R(-10f, 10f) : 0f); clutter++;
                }
            }
        }

        // ------------------------------------------------------------------ 11 litter and graffiti

        static GameObject Decal(string name, Material m, Vector3 pos, Quaternion rot, Vector3 size, Vector2? uvScale = null, Vector2? uvBias = null, float angle = 40f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var d = go.AddComponent<DecalProjector>();
            d.material = m; d.size = size; d.pivot = new Vector3(0, 0, size.z * 0.5f);
            d.drawDistance = 60f; d.fadeScale = 0.85f; d.startAngleFade = 180f - angle * 2f; d.endAngleFade = 180f - angle;
            if (uvScale.HasValue) d.uvScale = uvScale.Value;
            if (uvBias.HasValue) d.uvBias = uvBias.Value;
            go.isStatic = true;
            return go;
        }

        static Material DecalMat(string name, string tex)
        {
            if (Mats.TryGetValue("D_" + name, out var c)) return c;
            string path = $"{DecDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Shader Graphs/Decal")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("Base_Map", T(DecDir, tex));
            EditorUtility.SetDirty(m);
            Mats["D_" + name] = m;
            return m;
        }

        static void LitterAndGraffiti(List<(Vector2 a, Vector2 b, float w)> strips)
        {
            var sheet = DecalMat("D_Litter", "Litter_Sheet");
            Quaternion down(float yaw) => Quaternion.Euler(90f, yaw, 0f);
            for (int i = 0; i < 170; i++)
            {
                Vector2 p;
                int spot = rng.Next(4);
                if (spot == 0) p = new Vector2(R(-10f, 10f), R(8f, 54f));                         // the Midway
                else if (spot == 1) { float a = R(0, 6.28f), r = R(3f, 18f); p = FairgroundBlockout.CarouselCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                else if (spot == 2) p = new Vector2(R(-30f, 30f), R(58f, 76f));                   // gate plaza
                else { var s = strips[rng.Next(strips.Count)]; Vector2 d = (s.b - s.a).normalized, l = new Vector2(-d.y, d.x); p = s.a + (s.b - s.a) * R(0, 1f) + l * R(-s.w * 0.45f, s.w * 0.45f); }
                var bias = new Vector2(rng.Next(2) * 0.5f, rng.Next(2) * 0.5f);
                Decal("Litter", sheet, P(p.x, p.y, 0.6f), down(R(0, 360)), new Vector3(R(2.4f, 4.2f), R(2.4f, 4.2f), 1.6f), new Vector2(0.5f, 0.5f), bias); litter++;
            }
            // spray-painted warnings on the walls, the booths and the concrete
            string[] words = { "Grf_KestrelLied", "Grf_TurnBack", "Grf_NotSafe", "Grf_StayOut", "Grf_DontGoIn", "Grf_Auger", "Grf_GoHome" };
            var mats = words.Select(w => DecalMat(w, w)).ToArray();
            System.Action<float, float, float, float, float> wall = (x, z, yawProject, w, h) =>
            { Decal("Graffiti", mats[rng.Next(mats.Length)], new Vector3(x, R(1.3f, 2.1f), z), Quaternion.Euler(0, yawProject, 0), new Vector3(w, h, 1.6f), null, null, 70f); graffiti++; };
            for (int i = 0; i < 5; i++) wall(R(-70f, 70f), 88.9f, 0f, R(3.2f, 5f), R(1.1f, 1.7f));       // north wall (faces south)
            for (int i = 0; i < 4; i++) wall(-88.9f, R(-70f, 70f), 270f, R(3.2f, 5f), R(1.1f, 1.7f));    // west wall
            for (int i = 0; i < 3; i++) wall(R(-70f, -20f), -88.9f, 180f, R(3.2f, 5f), R(1.1f, 1.7f));   // south wall, dry side
            wall(78f, 39.2f, 180f, 4f, 1.4f); wall(54.5f, 33f, 270f, 4f, 1.4f);                        // garage and a trailer
            for (int i = 0; i < 4; i++)                                                              // on the ground, in front of the entrances
            {
                float x = new[] { -8f, 8f, -10f, 12f }[i], z = new[] { 12f, 12f, -53f, -64f }[i];
                Decal("Graffiti", mats[rng.Next(mats.Length)], P(x, z, 0.6f), down(new[] { 180f, 180f, 0f, 90f }[i]), new Vector3(4.2f, 1.4f, 1.6f)); graffiti++;
            }
        }
    }
}
#endif
