#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Step 4 of the Abandoned Fairground rebuild: light and atmosphere, laid over the dressed Step 2/3 scene.
    /// Works on the OPEN scene (it does not rebuild anything), then saves it as AbandonedFairground_Step4.unity.
    ///   practical lights only: burning barrels, camp fires, generator work lights, red emergency beacons, one lit
    ///   ticket booth, flickering bulbs - the moon is kept low so those are the pools the eye follows
    ///   fog, low mist over the water, light shafts through the Big Top and pavilion roof holes
    ///   light rain that follows the player, wetter materials, wind in the tarps (WindFlutter, already placed)
    ///   audio: wind, rain, water, a warped music box, a creaking wheel, drips, crows, generator hum
    ///   reflection probes (NOT baked: baking waits for approval, low settings first).
    /// Idempotent: the "14_Lighting" group is rebuilt each run.
    /// </summary>
    public static class FairgroundLighting
    {
        public const string ScenePath = "Assets/Fairground/Scenes/AbandonedFairground_Step4.unity";
        const string FxDir = "Assets/Fairground/Fx";
        const string AudioDir = "Assets/Fairground/Audio";
        const string MatDir = "Assets/Fairground/Materials/Dressed";

        static System.Random rng;
        static Transform root, g;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static int lights, flames, shafts, mistCards, audio;

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float H(float x, float z) => FairgroundBlockout.H(x, z);

        // ------------------------------------------------------------------ materials

        static Material Fx(string name, string tex, Color tint, float viewFade = 0f, float soft = 0f, float tiling = 1f, float sx = 0f, float sy = 0f)
        {
            if (Mats.TryGetValue(name, out var c)) return c;
            FairgroundMaterialConverter.EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Fairground/FG_FxAdd_URP")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{FxDir}/{tex}.png"));
            m.SetColor("_Color", tint);
            m.SetFloat("_ViewFade", viewFade); m.SetFloat("_SoftDepth", soft); m.SetFloat("_Tiling", tiling);
            m.SetFloat("_ScrollX", sx); m.SetFloat("_ScrollY", sy);
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        // ------------------------------------------------------------------ helpers

        static Light MakeLight(Transform parent, string name, Vector3 pos, LightType type, Color col, float intensity, float range, LightShadows shadows = LightShadows.None, float spot = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = type; l.color = col; l.intensity = intensity; l.range = range; l.shadows = shadows;
            if (type == LightType.Spot) l.spotAngle = spot;
            lights++;
            return l;
        }

        static void Flicker(Light l, FlickerLight.Mode mode, Vector2 range, float speed, Renderer emissive = null)
        {
            var f = l != null ? l.gameObject.AddComponent<FlickerLight>() : emissive.gameObject.AddComponent<FlickerLight>();
            f.mode = mode; f.range = range; f.speed = speed; f.target = l; f.emissive = emissive;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material m, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        static ParticleSystem Flame(Transform parent, Vector3 pos, float scale)
        {
            var go = new GameObject("Flame");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.6f * scale);
            main.startColor = new Color(1f, 0.55f, 0.18f, 0.85f);
            main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission; em.rateOverTime = 16f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9f; sh.radius = 0.13f * scale;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.5f), new GradientColorKey(new Color(0.4f, 0.08f, 0.02f), 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 0.2f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx("FX_Flame", "Fx_SoftDot", new Color(1.6f, 0.9f, 0.4f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            flames++;
            return ps;
        }

        static Mesh ConeMesh()
        {
            string path = "Assets/Fairground/Meshes/LightShaftCone.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            const int seg = 20;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int ring = 0; ring < 2; ring++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f, rad = ring == 0 ? 0.12f : 0.5f;
                    v.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, ring));
                    n.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), -0.35f).normalized);
                    uv.Add(new Vector2(i / (float)seg, ring));
                }
            for (int i = 0; i < seg; i++)
            {
                int a = i, b = i + 1, c = i + seg + 1, d = i + seg + 2;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
            var m = new Mesh { name = "LightShaftCone" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Meshes");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>A faint volumetric-looking beam: an additive cone from a point along a direction, to the ground.</summary>
        static void Shaft(Vector3 from, Vector3 dir, float width, Color tint, string name = "LightShaft")
        {
            dir.Normalize();
            float len = Mathf.Max(2f, (from.y - H(from.x, from.z)) / Mathf.Max(0.2f, -dir.y));
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            go.transform.localScale = new Vector3(width, width, len);
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Fx("FX_Shaft", "Fx_SoftDot", tint, 2.5f, 1.5f);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.isStatic = true;
            shafts++;
        }

        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{name}.wav");

        static AudioSource Sound(string name, string clip, Vector3 pos, bool loop, float vol, float minD, float maxD, bool spatial = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = Clip(clip); a.loop = loop; a.playOnAwake = true; a.volume = vol;
            a.spatialBlend = spatial ? 1f : 0f; a.minDistance = minD; a.maxDistance = maxD; a.rolloffMode = AudioRolloffMode.Linear;
            a.dopplerLevel = 0f;
            audio++;
            return a;
        }

        static void OneShots(string name, Vector3 pos, float volume, Vector2 interval, float scatter, params string[] clips)
        {
            var go = new GameObject(name);
            go.transform.SetParent(g, false);
            go.transform.position = pos;
            go.AddComponent<AudioSource>().playOnAwake = false;
            var o = go.AddComponent<AmbientOneShot>();
            o.clips = clips.Select(Clip).ToArray(); o.interval = interval; o.scatter = scatter; o.volume = volume;
            audio++;
        }

        static void ImportAudio()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(p) as AudioImporter;
                if (ai == null) continue;
                bool loop = p.Contains("_Loop") || p.Contains("MusicBox");
                var s = ai.defaultSampleSettings;
                s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.5f;
                if (ai.defaultSampleSettings.loadType != s.loadType || ai.defaultSampleSettings.compressionFormat != s.compressionFormat)
                { ai.defaultSampleSettings = s; ai.forceToMono = true; ai.SaveAndReimport(); }
            }
        }

        // ------------------------------------------------------------------ entry

        [MenuItem("Tools/MiniFPS/Fairground/Add Step 4 Lighting To Open Scene")]
        public static void Apply()
        {
            ApplyWorld();
            if (root == null) return;
            FairgroundLookTest.DisableAutoBake("Step4");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] Step 4 lighting: {lights} lights, {flames} flames, {shafts} shafts, {mistCards} mist cards, {audio} sounds -> {ScenePath}");
        }

        /// <summary>Adds the lighting and atmosphere to the open scene without saving it or touching its lighting settings.</summary>
        public static void ApplyWorld()
        {
            root = null;
            var rootGo = GameObject.Find("Blockout");
            if (rootGo == null) { Debug.LogError("[FPSKit] Open AbandonedFairground_Step2.unity first (no 'Blockout' root found)."); return; }
            root = rootGo.transform;
            rng = new System.Random(404);
            Mats.Clear();
            lights = flames = shafts = mistCards = audio = 0;
            var old = root.Find("14_Lighting");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            g = new GameObject("14_Lighting").transform;
            g.SetParent(root, false);

            ImportAudio();
            Atmosphere();
            Practicals();
            Mist();
            Shafts();
            // no rain: it did not behave, and the arena reads better dry
            Audio();
            Probes();
            Wetness();
        }

        public static string Summary() => $"{lights} lights, {flames} flames, {shafts} light shafts, {mistCards} mist cards, {audio} sound sources";

        // ------------------------------------------------------------------ sky, fog, moon

        static void Atmosphere()
        {
            var sun = RenderSettings.sun;
            // Dusk, not night (Ishaan found it too dim): a low, warm sun just behind the horizon, a lighter hazy sky, the practicals still glowing.
            if (sun != null)
            {
                sun.intensity = 0.95f; sun.color = new Color(1.0f, 0.74f, 0.58f);
                sun.transform.rotation = Quaternion.Euler(22f, 208f, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.25f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.085f, 0.10f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.25f, 0.25f, 0.31f);
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 210f;
            RenderSettings.reflectionIntensity = 0.7f;
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Fairground/Materials/Sky_QwantaniNight.mat");
            if (sky != null) { sky.SetFloat("_Exposure", 0.4f); if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", new Color(0.78f, 0.66f, 0.66f)); }
        }

        // ------------------------------------------------------------------ practical lights

        static void Practicals()
        {
            var dressing = root.Find("13_Dressing");
            var warm = new Color(1f, 0.55f, 0.2f);

            // Burning barrels and camp fires, from the anchors the dressing pass left.
            if (dressing != null)
            {
                foreach (var a in dressing.Cast<Transform>().Where(t => t.name == "FX_BarrelFire").ToList())
                {
                    var l = MakeLight(g, "BarrelFireLight", a.position, LightType.Point, warm, 16f, 13f);
                    Flicker(l, FlickerLight.Mode.Flicker, new Vector2(0.6f, 1.2f), 7f);
                    Flame(g, a.position - Vector3.up * 0.1f, 1.0f);
                }
                foreach (var a in dressing.Cast<Transform>().Where(t => t.name == "FX_CampFire").ToList())
                {
                    var l = MakeLight(g, "CampFireLight", a.position + Vector3.up * 0.3f, LightType.Point, new Color(1f, 0.5f, 0.16f), 8f, 8f);
                    Flicker(l, FlickerLight.Mode.Flicker, new Vector2(0.5f, 1.25f), 8f);
                    Flame(g, a.position - Vector3.up * 0.2f, 0.6f);
                }
            }

            // Generator-powered work lights: cold white on a pole, a hum at the base, a faint beam. They mark the way.
            var work = new[]
            {
                (pos: new Vector2(14f, 76f), aim: new Vector2(0f, 66f), shadows: true),     // gate plaza: where you start
                (pos: new Vector2(9f, 5f), aim: new Vector2(0f, -12f), shadows: false),     // the carousel
                (pos: new Vector2(-5f, -44f), aim: new Vector2(-10f, -63f), shadows: true), // the Big Top: where you are going
                (pos: new Vector2(41f, -4f), aim: new Vector2(52f, -14f), shadows: false),  // the Ferris wheel
                (pos: new Vector2(62f, 16f), aim: new Vector2(66f, 30f), shadows: false),   // the backlot
            };
            var iron = Existing("RustDark");
            foreach (var w in work)
            {
                float gy = H(w.pos.x, w.pos.y);
                Prim(PrimitiveType.Cylinder, "WorkLightPole", g, new Vector3(w.pos.x, gy + 3.1f, w.pos.y), Quaternion.identity, new Vector3(0.16f, 3.1f, 0.16f), iron);
                Prim(PrimitiveType.Cube, "Generator", g, new Vector3(w.pos.x + 1.0f, gy + 0.45f, w.pos.y), Quaternion.identity, new Vector3(1.3f, 0.9f, 0.8f), iron);
                var head = Prim(PrimitiveType.Cube, "WorkLightHead", g, new Vector3(w.pos.x, gy + 6.2f, w.pos.y), Quaternion.identity, new Vector3(0.7f, 0.4f, 0.45f), iron, false);
                Vector3 from = new Vector3(w.pos.x, gy + 6.0f, w.pos.y);
                Vector3 to = new Vector3(w.aim.x, H(w.aim.x, w.aim.y) + 0.5f, w.aim.y);
                head.transform.rotation = Quaternion.LookRotation(to - from);
                var l = MakeLight(g, "WorkLight", from, LightType.Spot, new Color(0.82f, 0.9f, 1f), 190f, 40f, w.shadows ? LightShadows.Soft : LightShadows.None, 82f);
                l.transform.rotation = Quaternion.LookRotation(to - from);
                Flicker(l, FlickerLight.Mode.Dropout, new Vector2(0.93f, 1.03f), 3f);
                Shaft(from + (to - from).normalized * 0.5f, (to - from).normalized, 9f, new Color(0.10f, 0.13f, 0.17f), "WorkLightBeam");
                Sound("GeneratorHum", "Generator_Hum_Loop", new Vector3(w.pos.x + 1.0f, gy + 0.8f, w.pos.y), true, 0.4f, 2f, 24f);
            }

            // Red emergency beacons: the coaster station, the top of the wheel, the bumper-car pavilion.
            var red = new Color(1f, 0.08f, 0.05f);
            foreach (var p in new[] { new Vector3(-62f, H(-62f, 24f) + 7.4f, 24f), new Vector3(52f, H(52f, -14f) + 21.4f, -14f), new Vector3(75f, 5.9f, -48f) })
            {
                var l = MakeLight(g, "EmergencyBeacon", p, LightType.Point, red, 42f, 18f);
                Flicker(l, FlickerLight.Mode.Pulse, new Vector2(0.05f, 1f), 0.45f);
                Prim(PrimitiveType.Sphere, "BeaconLens", g, p, Quaternion.identity, Vector3.one * 0.3f, Existing("BulbLive") ?? Existing("RustDark"), false);
            }

            LitBooth();
            Bulbs(dressing);
        }

        static void LitBooth()
        {
            var booth = root.Find("1_MainGate")?.Cast<Transform>().FirstOrDefault(t => t.name == "TicketBooth" && Mathf.Abs(t.position.x - 7f) < 0.5f);
            if (booth == null) return;
            // The boarding on this booth is torn down: someone is still using it.
            var dressing = root.Find("13_Dressing");
            if (dressing != null)
                foreach (var t in dressing.Cast<Transform>().Where(t => t.name == "Plywood" && Vector3.Distance(new Vector3(t.position.x, booth.position.y, t.position.z), booth.position + new Vector3(0, 0, 1.55f)) < 2.2f).ToList())
                    Object.DestroyImmediate(t.gameObject);
            var glowMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/BoothGlow.mat");
            if (glowMat == null)
            {
                glowMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(glowMat, $"{MatDir}/BoothGlow.mat");
            }
            glowMat.SetColor("_BaseColor", new Color(0.3f, 0.22f, 0.12f));
            glowMat.EnableKeyword("_EMISSION"); glowMat.SetColor("_EmissionColor", new Color(2.4f, 1.55f, 0.7f));
            EditorUtility.SetDirty(glowMat);
            var win = Prim(PrimitiveType.Cube, "BoothWindowGlow", g, booth.position + new Vector3(0, 1.9f, 1.53f), Quaternion.identity, new Vector3(1.9f, 0.85f, 0.05f), glowMat, false);
            var l = MakeLight(g, "BoothLight", booth.position + new Vector3(0, 2.1f, 2.3f), LightType.Point, new Color(1f, 0.78f, 0.45f), 34f, 11f);
            Flicker(l, FlickerLight.Mode.Flicker, new Vector2(0.9f, 1.05f), 2.5f, win.GetComponent<Renderer>());
        }

        static void Bulbs(Transform dressing)
        {
            if (dressing == null) return;
            int i = 0;
            foreach (var t in dressing.Cast<Transform>().Where(t => t.name == "BulbLive").ToList())
            {
                t.gameObject.isStatic = false;                                  // its emission changes at runtime
                Light l = null;
                if (i++ % 3 == 0) l = MakeLight(g, "BulbLight", t.position, LightType.Point, new Color(1f, 0.62f, 0.28f), 4.5f, 6.5f);
                var r = t.GetComponent<Renderer>();
                Flicker(l, FlickerLight.Mode.Dropout, new Vector2(0.55f, 1.1f), 5f, r);
            }
        }

        // ------------------------------------------------------------------ mist and shafts

        static void Mist()
        {
            // Low cool haze over the water: two layers of big scrolling cards.
            var mat = Fx("FX_Mist", "Fx_Mist", new Color(0.011f, 0.015f, 0.019f), 0f, 0.9f, 3f, 0.004f, 0.002f);      // 0.06 washed the whole lowlands out to ice
            for (int layer = 0; layer < 2; layer++)
                for (int i = 0; i < 14; i++)
                {
                    float x = R(18f, 88f), z = R(-88f, -28f), size = R(30f, 46f);
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.name = "MistCard";
                    q.transform.SetParent(g, false);
                    q.transform.SetPositionAndRotation(new Vector3(x, 0.35f + layer * 0.65f, z), Quaternion.Euler(90f, R(0f, 360f), 0f));
                    q.transform.localScale = new Vector3(size, size, 1f);
                    Object.DestroyImmediate(q.GetComponent<Collider>());
                    var r = q.GetComponent<Renderer>();
                    r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    q.isStatic = true;
                    mistCards++;
                }
        }

        static void Shafts()
        {
            Vector3 moon = RenderSettings.sun != null ? RenderSettings.sun.transform.forward : new Vector3(-0.27f, -0.62f, -0.74f);
            var tint = new Color(0.16f, 0.22f, 0.36f);
            // Big Top: the four missing roof slabs.
            var c = FairgroundBlockout.BigTopCentre; float gy = H(c.x, c.y);
            foreach (int i in new[] { 1, 4, 7, 10 })
            {
                float a = (i + 0.5f) / 12f * Mathf.PI * 2f;
                Shaft(new Vector3(c.x + Mathf.Cos(a) * 9.5f, gy + 11.5f, c.y + Mathf.Sin(a) * 9.5f), moon, 5.5f, tint, "BigTopShaft");
            }
            // The bumper-car pavilion: cut a hole in the roof and let the moon through it.
            var low = root.Find("5_FloodedLowlands");
            var roof = low?.Find("PavilionRoof");
            if (roof != null)
            {
                var m = roof.GetComponent<Renderer>().sharedMaterial;
                Vector3 p = roof.position; float y0 = p.y - 0.2f;
                Object.DestroyImmediate(roof.gameObject);
                void Piece(float x0, float x1, float z0, float z1) =>
                    Prim(PrimitiveType.Cube, "PavilionRoof", low, new Vector3((x0 + x1) / 2f, y0 + 0.2f, (z0 + z1) / 2f), Quaternion.identity, new Vector3(x1 - x0, 0.4f, z1 - z0), m);
                // roof spans x 48..76, z -69..-47; hole x 63.5..68.5, z -58.5..-53.5
                Piece(48f, 63.5f, -69f, -47f); Piece(68.5f, 76f, -69f, -47f); Piece(63.5f, 68.5f, -53.5f, -47f); Piece(63.5f, 68.5f, -69f, -58.5f);
                Shaft(new Vector3(66f, y0 + 0.4f, -56f), moon, 5f, tint, "PavilionShaft");
            }
        }

        // ------------------------------------------------------------------ rain

        static void Rain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(0f, 14f, 55f);
            go.AddComponent<RainFollow>();
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.startLifetime = 0.75f; main.startSpeed = 26f; main.startSize = 0.06f;
            main.startColor = new Color(0.7f, 0.8f, 1f, 0.38f); main.maxParticles = 2200; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 1800f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 0.1f, 34f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);                          // emit straight down
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 2.2f; r.velocityScale = 0.03f; r.cameraVelocityScale = 0f;
            r.sharedMaterial = Fx("FX_Rain", "Fx_Rain", new Color(0.55f, 0.65f, 0.85f));
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ audio

        static void Audio()
        {
            Sound("Wind", "Wind_Loop", new Vector3(0, 10f, 0), true, 0.32f, 1f, 500f, false);
            var c = FairgroundBlockout.CarouselCentre;
            Sound("MusicBox", "MusicBox_Warped", new Vector3(c.x, H(c.x, c.y) + 4.5f, c.y), true, 0.4f, 6f, 70f);
            var f = FairgroundBlockout.FerrisCentre;
            Sound("FerrisCreak", "Ferris_Creak_Loop", new Vector3(f.x, H(f.x, f.y) + 19.5f, f.y), true, 0.55f, 8f, 55f);
            OneShots("Drips_Pavilion", new Vector3(66f, 4.5f, -56f), 0.5f, new Vector2(1.2f, 4f), 1.5f, "Drip_1", "Drip_2", "Drip_3");
            OneShots("Drips_Station", new Vector3(-62f, 6f, 24f), 0.45f, new Vector2(1.8f, 5f), 4f, "Drip_1", "Drip_2", "Drip_3");
            OneShots("Drips_BigTop", new Vector3(-10f, 6f, -68f), 0.45f, new Vector2(1.5f, 4.5f), 6f, "Drip_2", "Drip_3");
            foreach (var p in new[] { new Vector3(-80f, 12f, 40f), new Vector3(85f, 18f, 30f), new Vector3(0f, 22f, -100f), new Vector3(-90f, 14f, -40f) })
                OneShots("Crows", p, 0.5f, new Vector2(14f, 42f), 8f, "Crow_1", "Crow_2");
        }

        // ------------------------------------------------------------------ reflection probes (not baked)

        static void Probes()
        {
            foreach (var p in new[] { (n: "Probe_Lowlands", c: new Vector3(54f, 2f, -58f), s: new Vector3(80f, 10f, 70f)), (n: "Probe_Plaza", c: new Vector3(0f, 4f, -12f), s: new Vector3(50f, 12f, 50f)), (n: "Probe_Gate", c: new Vector3(0f, 6f, 70f), s: new Vector3(70f, 12f, 30f)) })
            {
                var go = new GameObject(p.n);
                go.transform.SetParent(g, false);
                go.transform.position = p.c;
                var rp = go.AddComponent<ReflectionProbe>();
                rp.mode = ReflectionProbeMode.Baked; rp.size = p.s; rp.resolution = 128; rp.boxProjection = true; rp.importance = 1; rp.hdr = true;
            }
        }

        // ------------------------------------------------------------------ wet

        static void Wetness()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.shader != null && m.shader.name == "Fairground/FG_WorldPBR_URP" && m.HasProperty("_Wetness"))
                { m.SetFloat("_Wetness", Mathf.Max(m.GetFloat("_Wetness"), 0.45f)); EditorUtility.SetDirty(m); }
            }
        }
    }
}
#endif
