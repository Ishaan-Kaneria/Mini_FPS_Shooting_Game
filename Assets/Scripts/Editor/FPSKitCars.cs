#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's own cars, from Blender (Tools/blender/make_cars.py), into prefabs. Each model gives three: a parked car merged into one mesh and three materials (paint, atlas, glass: a street holds two hundred),
    /// a traffic car (parts apart, kinematic, the town drives it) and a drivable one (made per spot, WheelColliders). The paint is a material on a slot, chosen per car.
    /// <see cref="PatchDesert"/> puts them into the saved desert scene without rebuilding it: cars are dynamically lit and off the navmesh bake, so nothing needs baking again.
    /// </summary>
    public static class FPSKitCars
    {
        public const string Folder = "Assets/Models/Cars";
        static string Out => FPSKitTownAssets.Out + "/Cars";

        /// <summary>A kind of car in the town: its model, whether it has the taxi sign, how often it appears, how it drives.</summary>
        public struct Kind
        {
            public string name, model; public bool taxi; public int weight;
            public float mass, torque, top; public DrivableCar.Driven drive; public float finalDrive;
        }

        public static readonly Kind[] Kinds =
        {
            new Kind { name = "Saloon", model = "Saloon", weight = 5, mass = 1350f, torque = 215f, top = 44f, drive = DrivableCar.Driven.Front, finalDrive = 3.9f },
            new Kind { name = "OldSaloon", model = "OldSaloon", weight = 4, mass = 1280f, torque = 165f, top = 38f, drive = DrivableCar.Driven.Rear, finalDrive = 3.7f },
            new Kind { name = "Taxi", model = "OldSaloon", taxi = true, weight = 3, mass = 1280f, torque = 165f, top = 38f, drive = DrivableCar.Driven.Rear, finalDrive = 3.7f },
            new Kind { name = "Hatch", model = "Hatch", weight = 4, mass = 1080f, torque = 150f, top = 40f, drive = DrivableCar.Driven.Front, finalDrive = 4.1f },
            new Kind { name = "OffRoader", model = "OffRoader", weight = 3, mass = 2000f, torque = 360f, top = 40f, drive = DrivableCar.Driven.All, finalDrive = 3.9f },
            new Kind { name = "Pickup", model = "Pickup", weight = 4, mass = 1850f, torque = 330f, top = 38f, drive = DrivableCar.Driven.Rear, finalDrive = 3.9f },
            new Kind { name = "Minibus", model = "Minibus", weight = 2, mass = 2100f, torque = 280f, top = 33f, drive = DrivableCar.Driven.Rear, finalDrive = 4.3f },
        };

        /// <summary>Colours as sold: a bright street, with the white, silver and beige a hot country actually buys in among them. (name, sRGB, metallic, smoothness)</summary>
        static readonly (string name, Color c, float metal, float smooth)[] PaintTable =
        {
            ("Red", new Color(0.66f, 0.05f, 0.05f), 0.15f, 0.82f), ("Maroon", new Color(0.33f, 0.04f, 0.07f), 0.3f, 0.82f), ("Orange", new Color(0.86f, 0.33f, 0.05f), 0.1f, 0.82f),
            ("Yellow", new Color(0.91f, 0.68f, 0.07f), 0.1f, 0.82f), ("Lime", new Color(0.42f, 0.60f, 0.14f), 0.15f, 0.8f), ("Green", new Color(0.05f, 0.31f, 0.20f), 0.3f, 0.82f),
            ("Teal", new Color(0.03f, 0.41f, 0.45f), 0.3f, 0.84f), ("Sky", new Color(0.30f, 0.58f, 0.82f), 0.25f, 0.84f), ("Blue", new Color(0.05f, 0.20f, 0.58f), 0.35f, 0.84f),
            ("Navy", new Color(0.03f, 0.07f, 0.20f), 0.35f, 0.84f), ("Purple", new Color(0.27f, 0.11f, 0.45f), 0.35f, 0.84f), ("White", new Color(0.88f, 0.88f, 0.85f), 0f, 0.8f),
            ("Pearl", new Color(0.82f, 0.80f, 0.74f), 0.15f, 0.84f), ("Silver", new Color(0.62f, 0.63f, 0.64f), 0.85f, 0.72f), ("Graphite", new Color(0.21f, 0.22f, 0.23f), 0.85f, 0.72f),
            ("Black", new Color(0.025f, 0.025f, 0.03f), 0.1f, 0.88f), ("Sand", new Color(0.66f, 0.54f, 0.36f), 0.2f, 0.72f), ("Mint", new Color(0.52f, 0.76f, 0.64f), 0.1f, 0.8f),
        };

        const string TaxiPaint = "Yellow";

        static string[] Names(Mesh mesh, string part)
        {
            // Blender's slots are Paint, Atlas, Glass; a mesh keeps those it uses, in that order.
            if (mesh.subMeshCount >= 3) return new[] { "Paint", "Atlas", "Glass" };
            if (mesh.subMeshCount == 2) return new[] { "Paint", "Atlas" };
            if (part.StartsWith("Glass_")) return new[] { "Glass" };
            return new[] { "Atlas" };
        }

        // ------------------------------------------------------------------ materials
        static Dictionary<string, Material> _mats;

        static Material Lit(string name, System.Action<Material> set, bool transparent = false)
        {
            string path = $"{Out}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            set(m);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> Mats()
        {
            if (_mats != null && _mats["Atlas"] != null) return _mats;
            Ensure(Out);
            string tex = Folder + "/Textures";
            Texture2D Tex(string file, bool srgb, int size)
            {
                string path = $"{tex}/{file}";
                if (AssetImporter.GetAtPath(path) is TextureImporter t && (t.sRGBTexture != srgb || t.maxTextureSize != size))
                { t.sRGBTexture = srgb; t.maxTextureSize = size; t.alphaSource = TextureImporterAlphaSource.FromInput; t.SaveAndReimport(); }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var atlas = Tex("CarAtlas.png", true, 2048); var ms = Tex("CarAtlas_MS.png", false, 512); var dust = Tex("CarDust.png", true, 512);
            _mats = new Dictionary<string, Material>
            {
                ["Atlas"] = Lit("CarAtlas", m =>
                {
                    m.SetTexture("_BaseMap", atlas); m.SetColor("_BaseColor", Color.white);
                    m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f);
                }),
                ["Glass"] = Lit("CarGlass", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.05f, 0.08f, 0.08f, 0.20f)); m.SetFloat("_Smoothness", 0.96f); m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_BlendModePreserveSpecular", 1f); m.SetFloat("_AlphaClip", 0f); m.SetFloat("_Cull", 0f); m.SetFloat("_ZWrite", 0f);
                    m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
                    m.SetShaderPassEnabled("DepthOnly", false); m.SetShaderPassEnabled("ShadowCaster", false);
                }),
                ["GlowBrake"] = Lit("CarGlowBrake", m => Glow(m, new Color(1f, 0.06f, 0.05f), 3.5f)),
                ["GlowReverse"] = Lit("CarGlowReverse", m => Glow(m, new Color(1f, 0.82f, 0.58f), 2.2f)),
                ["GlowHead"] = Lit("CarGlowHead", m => Glow(m, new Color(1f, 0.95f, 0.85f), 3f)),
            };
            foreach (var p in PaintTable)
            {
                var local = p;
                _mats["Paint" + p.name] = Lit("Paint_" + p.name, m =>
                {
                    m.SetTexture("_BaseMap", dust); m.SetTextureScale("_BaseMap", Vector2.one);
                    m.SetColor("_BaseColor", local.c); m.SetFloat("_Metallic", local.metal); m.SetFloat("_Smoothness", local.smooth);
                    m.SetFloat("_EnvironmentReflections", 1f); m.SetFloat("_SpecularHighlights", 1f);
                });
            }
            AssetDatabase.SaveAssets();
            return _mats;
        }

        static void Glow(Material m, Color c, float strength)
        {
            m.SetColor("_BaseColor", c * 0.6f); m.SetFloat("_Smoothness", 0.7f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * strength);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        /// <summary>The paint a car gets. Taxis are yellow; the rest draw from the whole table.</summary>
        public static Material PaintFor(Kind kind, System.Random rng)
        {
            var mats = Mats();
            if (kind.taxi) return mats["Paint" + TaxiPaint];
            return mats["Paint" + PaintTable[rng.Next(PaintTable.Length)].name];
        }

        public static Material Paint(string colour) => Mats()["Paint" + colour];

        public static Material[] AllPaints() { var m = Mats(); return PaintTable.Select(p => m["Paint" + p.name]).ToArray(); }

        // ------------------------------------------------------------------ the model
        static void Ensure(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static GameObject Model(string model)
        {
            string fbx = $"{Folder}/{model}.fbx";
            if (AssetImporter.GetAtPath(fbx) is ModelImporter mi)
            {
                bool change = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.animationType != ModelImporterAnimationType.None || !mi.isReadable
                              || mi.importNormals != ModelImporterNormals.Import || mi.importTangents != ModelImporterTangents.None || mi.importBlendShapes || mi.meshCompression != ModelImporterMeshCompression.Off;
                if (change)
                {
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.importAnimation = false;
                    mi.isReadable = true;            // CarGlass reads its windows; this is what the merged meshes are made from
                    mi.importNormals = ModelImporterNormals.Import;
                    mi.importTangents = ModelImporterTangents.None;
                    mi.importBlendShapes = false;
                    mi.meshCompression = ModelImporterMeshCompression.Off;
                    mi.SaveAndReimport();
                }
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (go == null) Debug.LogError($"[FPSKit] car model missing: {fbx}. Run Tools/blender/make_cars.py.");
            return go;
        }

        static GameObject Instance(string model)
        {
            var src = Model(model);
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static void Dress(GameObject root, Material paint)
        {
            var m = Mats();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var names = Names(mf.sharedMesh, r.name);
                r.sharedMaterials = names.Select(n => n == "Paint" ? paint : m[n]).ToArray();
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                bool glass = r.name.StartsWith("Glass_");
                r.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        struct Measured { public Bounds body; public float wheelRadius, wheelbase; public Vector3[] wheels; }

        static Measured Measure(GameObject root)
        {
            var m = new Measured { wheels = new Vector3[4] };
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Body")) continue;
                if (!any) { m.body = r.bounds; any = true; } else m.body.Encapsulate(r.bounds);
            }
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                var w = Find(root.transform, names[i]);
                if (w == null) continue;
                m.wheels[i] = w.position;
                m.wheelRadius = Mathf.Max(m.wheelRadius, w.position.y);
            }
            m.wheelbase = Mathf.Abs(m.wheels[0].z - m.wheels[2].z);
            return m;
        }

        /// <summary>The body to the waistline and the roof, with the windows open between them: rounds through the glass reach whoever is inside. Metal, for the sparks.</summary>
        static void Colliders(GameObject root, Bounds b)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            float belt = b.size.y * 0.56f;
            var box = root.AddComponent<BoxCollider>();
            // Its floor is the sills, not the ground: a kerb is for the wheels to climb. A box to the ground stopped the car dead on the roundabout's kerb, wheels spinning.
            float sill = Mathf.Min(b.size.y * 0.24f, 0.36f);
            box.center = new Vector3(0f, (belt + sill) * 0.5f, b.center.z);
            box.size = new Vector3(b.size.x * 0.97f, belt - sill, b.size.z * 0.98f);
            box.sharedMaterial = FPSKitImpactAssets.Surface("Metal");
            var roof = root.AddComponent<BoxCollider>();
            roof.center = new Vector3(0f, b.size.y * 0.965f, b.center.z - b.size.z * 0.02f);
            roof.size = new Vector3(b.size.x * 0.72f, b.size.y * 0.07f, b.size.z * 0.38f);
            roof.sharedMaterial = box.sharedMaterial;
        }

        static void LayerAndTag(GameObject root, string layer)
        {
            int l = LayerMask.NameToLayer(layer);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (l >= 0) t.gameObject.layer = l;
                t.gameObject.tag = "Metal";
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
        }

        // ------------------------------------------------------------------ parts apart: traffic and drivable
        /// <summary>The model with its parts in place and named: doors holding their glass, the cheap levels of detail behind an LOD group, a CarLook and a CarGlass.</summary>
        static GameObject Rig(Kind kind, Material paint)
        {
            var root = new GameObject(kind.name);
            var inst = Instance(kind.model);
            if (inst == null) { Object.DestroyImmediate(root); return null; }
            inst.name = "Model";
            inst.transform.SetParent(root.transform, false);
            var model = inst.transform;
            var sign = Find(model, "Taxi_Sign");
            if (sign != null && !kind.taxi) Object.DestroyImmediate(sign.gameObject);
            Dress(inst, paint);
            var m = Measure(inst);

            string[] sides = { "FL", "FR", "RL", "RR" };
            var look = root.AddComponent<CarLook>();
            look.model = model;
            look.fixedPaint = kind.taxi;
            look.wheelRadius = m.wheelRadius;
            look.wheelbase = m.wheelbase;
            for (int i = 0; i < 4; i++)
            {
                look.wheels[i] = Find(model, "Wheel_" + sides[i]);
                var door = Find(model, "Door_" + sides[i]);
                look.doors[i] = door;
                look.doorOpen[i] = sides[i][1] == 'L' ? -64f : 64f;
                if (door == null) continue;
                var glass = Find(model, "Glass_" + sides[i]);
                if (glass != null) glass.SetParent(door, true);
                var card = Find(model, "Card_" + sides[i]);
                if (card != null) card.SetParent(door, true);
            }
            look.steerWheel = Find(model, "SteerWheel");
            look.needleSpeed = Find(model, "Needle_Speed");
            look.needleRpm = Find(model, "Needle_Rpm");
            var axisEnd = Find(model, "Steer_Axis");
            if (look.steerWheel != null && axisEnd != null)
            {
                look.steerHub = root.transform.InverseTransformPoint(look.steerWheel.position);
                look.steerAxis = (axisEnd.position - look.steerWheel.position).normalized;
                // In the wheel's parent's frame, for its local rotation: the model has no turn of its own.
            }
            var eye = Find(model, "Seat_Eye");
            if (eye != null) look.seatEye = eye.position;
            var pipe = Find(model, "Exhaust");
            if (pipe != null) look.exhaust = pipe.position;

            var mats = Mats();
            Renderer Lamp(string name, string mat)
            {
                var t = Find(model, name);
                if (t == null || !t.TryGetComponent<MeshRenderer>(out var r)) return null;
                r.sharedMaterial = mats[mat];
                r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off;
                return r;
            }
            look.glowBrake = Lamp("Glow_Brake", "GlowBrake");
            look.glowReverse = Lamp("Glow_Reverse", "GlowReverse");
            look.glowHead = Lamp("Glow_Head", "GlowHead");

            // Everything with paint on it: the body, the doors, and the far levels.
            var painted = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterials.Length >= 2 && r.sharedMaterials[0] == paint) painted.Add(r);
            look.painted = painted.ToArray();

            // Levels of detail: everything near; the merged ones far.
            var near = inst.GetComponentsInChildren<MeshRenderer>(true).Where(r => !r.name.StartsWith("LOD") && !r.name.StartsWith("Glow_")).Cast<Renderer>().ToArray();
            var lod1 = Find(model, "LOD1_All"); var lod2 = Find(model, "LOD2_All");
            var far1 = lod1 != null ? lod1.GetComponent<Renderer>() : null; var far2 = lod2 != null ? lod2.GetComponent<Renderer>() : null;
            var group = inst.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.11f, near),
                new LOD(0.022f, far1 != null ? (sign != null ? new[] { far1, sign.GetComponent<Renderer>() } : new[] { far1 }) : near),
                new LOD(0.007f, far2 != null ? new[] { far2 } : near),
            });
            group.RecalculateBounds();

            var glassOn = root.AddComponent<CarGlass>();
            var windows = new List<CarGlass.Window>();
            foreach (var part in new[] { "Glass_Front", "Glass_Rear", "Glass_FL", "Glass_FR", "Glass_RL", "Glass_RR" })
            {
                var t = Find(model, part);
                if (t == null) continue;
                windows.Add(new CarGlass.Window { name = part, frame = t, filter = t.GetComponent<MeshFilter>(), windscreen = part == "Glass_Front" });
            }
            glassOn.windows = windows.ToArray();
            glassOn.farRenderers = new[] { far1, far2 }.Where(r => r != null).ToArray();
            glassOn.farGlassSlot = 2;
            Colliders(root, m.body);
            return root;
        }

        public static GameObject TrafficPrefab(Kind kind)
        {
            Ensure(Out);
            var root = Rig(kind, PaintFor(kind, new System.Random(kind.name.GetHashCode())));
            if (root == null) return null;
            var m = Measure(root.transform.Find("Model").gameObject);
            var b = m.body;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var obs = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obs.center = new Vector3(0f, b.size.y * 0.5f, 0f); obs.size = new Vector3(b.size.x * 0.95f, b.size.y * 0.9f, b.size.z * 0.97f);
            obs.carving = false;
            var car = root.AddComponent<TrafficCar>();
            car.halfLength = b.size.z * 0.5f;
            car.halfWidth = b.size.x * 0.5f;
            var look = root.GetComponent<CarLook>();
            // The driver's hips sit about 0.7 m under the eye (a seated person's height there).
            car.seatLocal = look.seatEye - new Vector3(0f, 1.14f, 0.05f);
            var strike = root.AddComponent<VehicleStrike>();
            strike.halfLength = car.halfLength; strike.halfWidth = car.halfWidth;
            LayerAndTag(root, "Vehicle");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/Traffic_{kind.name}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>One car to drive, made in the scene: the rig with a body and four wheel colliders where its wheels are.</summary>
        public static GameObject MakeDrivable(Transform parent, Kind kind, Material paint, Vector3 pos, float yaw)
        {
            var root = Rig(kind, paint);
            if (root == null) return null;
            root.name = "Drivable_" + kind.name;
            root.transform.SetParent(parent, false);
            var model = root.transform.Find("Model");
            var m = Measure(model.gameObject);
            root.AddComponent<Rigidbody>();
            var car = root.AddComponent<DrivableCar>();
            car.mass = kind.mass; car.peakTorque = kind.torque; car.topSpeed = kind.top; car.driven = kind.drive; car.finalDrive = kind.finalDrive;
            var strike = root.AddComponent<VehicleStrike>();
            strike.halfLength = m.body.size.z * 0.5f; strike.halfWidth = m.body.size.x * 0.5f;
            string[] names = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
            string[] sides = { "FL", "FR", "RL", "RR" };
            var cols = new WheelCollider[4];
            var look = root.GetComponent<CarLook>();
            for (int k = 0; k < 4; k++)
            {
                var go = new GameObject("Wheel" + names[k]);
                go.transform.SetParent(root.transform, false);
                var at = look.wheels[k] != null ? look.wheels[k].position : Vector3.zero;
                go.transform.localPosition = at + Vector3.up * 0.09f;
                var wc = go.AddComponent<WheelCollider>();
                wc.radius = m.wheelRadius;
                wc.mass = kind.mass * 0.015f;
                wc.suspensionDistance = 0.2f;
                cols[k] = wc;
            }
            car.frontLeft = cols[0]; car.frontRight = cols[1]; car.rearLeft = cols[2]; car.rearRight = cols[3];
            car.wheelVisuals = look.wheels.ToArray();
            car.exitOffset = new Vector3(-(m.body.size.x * 0.5f + 0.9f), 0f, 0.3f);
            // Drivable cars' height is the same box as traffic's; the eye comes from the model.
            LayerAndTag(root, "Vehicle");
            var box = root.GetComponents<BoxCollider>()[0];
            var obs = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box; obs.center = new Vector3(0f, m.body.size.y * 0.5f, 0f); obs.size = new Vector3(box.size.x, m.body.size.y * 0.9f, box.size.z);
            obs.carving = false;
            var mod = root.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            mod.ignoreFromBuild = true;
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return root;
        }

        // ------------------------------------------------------------------ merged: parked
        static readonly Dictionary<string, GameObject> _parked = new Dictionary<string, GameObject>();

        /// <summary>
        /// A parked car: every part of LOD0 merged into one mesh of three submeshes, with the record of which glass triangles are which window, and the Blender file's two cheaper levels. Shared by all of a kind;
        /// the paint is the first slot, per car. No static batching: a broken window needs its own copy of the mesh, and three draws a car does not need it.
        /// </summary>
        public static GameObject ParkedPrefab(Kind kind)
        {
            if (_parked.TryGetValue(kind.name, out var cached) && cached != null) return cached;
            Ensure(Out);
            var paint = PaintFor(kind, new System.Random(1));
            var inst = Instance(kind.model);
            if (inst == null) return null;
            if (!kind.taxi) { var sign = Find(inst.transform, "Taxi_Sign"); if (sign != null) Object.DestroyImmediate(sign.gameObject); }
            var groups = new Dictionary<string, List<CombineInstance>> { ["Paint"] = new List<CombineInstance>(), ["Atlas"] = new List<CombineInstance>(), ["Glass"] = new List<CombineInstance>() };
            var glassParts = new List<(string name, int indexCount)>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                string n = mf.name;
                if (n.StartsWith("LOD") || n.StartsWith("Glow_") || mf.sharedMesh == null) continue;
                var names = Names(mf.sharedMesh, n);
                for (int s = 0; s < names.Length && s < mf.sharedMesh.subMeshCount; s++)
                {
                    groups[names[s]].Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = mf.transform.localToWorldMatrix });
                    if (names[s] == "Glass") glassParts.Add((n, (int)mf.sharedMesh.GetIndexCount(s)));
                }
            }
            Mesh Flat(List<CombineInstance> list)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(list.ToArray(), true, true);
                return part;
            }
            var parts = new[] { Flat(groups["Paint"]), Flat(groups["Atlas"]), Flat(groups["Glass"]) };
            var merged = new Mesh { name = $"Parked_{kind.name}_LOD0", indexFormat = IndexFormat.UInt32 };
            merged.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p, transform = Matrix4x4.identity }).ToArray(), false, false);
            foreach (var p in parts) Object.DestroyImmediate(p);
            MeshUtility.SetMeshCompression(merged, ModelImporterMeshCompression.Off);
            string meshPath = $"{Out}/Parked_{kind.name}_LOD0.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old != null) { EditorUtility.CopySerialized(merged, old); Object.DestroyImmediate(merged); merged = old; }
            else AssetDatabase.CreateAsset(merged, meshPath);

            var root = new GameObject("Parked_" + kind.name);
            var m = Mats();
            MeshRenderer Level(string name, Mesh mesh)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { paint, m["Atlas"], m["Glass"] };
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                return r;
            }
            var near = Level("LOD0", merged);
            var far1 = Level("LOD1", Find(inst.transform, "LOD1_All").GetComponent<MeshFilter>().sharedMesh);
            var far2 = Level("LOD2", Find(inst.transform, "LOD2_All").GetComponent<MeshFilter>().sharedMesh);
            far2.shadowCastingMode = ShadowCastingMode.Off;
            // The sign says what it is from across the street: it stays through the middle level, on the atlas like the rest.
            var nearList = new List<Renderer> { far1 };
            var signPart = Find(inst.transform, "Taxi_Sign");
            if (signPart != null)
            {
                var signLevel = new GameObject("Sign");
                signLevel.transform.SetParent(root.transform, false);
                signLevel.AddComponent<MeshFilter>().sharedMesh = signPart.GetComponent<MeshFilter>().sharedMesh;
                signLevel.transform.position = signPart.position;
                var sr = signLevel.AddComponent<MeshRenderer>();
                sr.sharedMaterial = m["Atlas"];
                nearList.Add(sr);
            }
            var lod = root.AddComponent<LODGroup>();
            // Past where the shape can be told from the fine one at each step (his note: blocky at a distance, 2026-11-06).
            lod.SetLODs(new[] { new LOD(0.11f, new Renderer[] { near }), new LOD(0.022f, nearList.ToArray()), new LOD(0.007f, new Renderer[] { far2 }) });
            lod.RecalculateBounds();

            var glass = root.AddComponent<CarGlass>();
            var windows = new List<CarGlass.Window>();
            int at = 0;
            foreach (var (name, count) in glassParts)
            {
                windows.Add(new CarGlass.Window { name = name, frame = near.transform, filter = near.GetComponent<MeshFilter>(), submesh = 2, start = at, count = count, windscreen = name == "Glass_Front" });
                at += count;
            }
            glass.windows = windows.ToArray();
            glass.farRenderers = new Renderer[] { far1, far2 };
            glass.farGlassSlot = 2;
            Object.DestroyImmediate(inst);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/Parked_{kind.name}.prefab");
            Object.DestroyImmediate(root);
            _parked[kind.name] = saved;
            return saved;
        }

        /// <summary>A parked car in a scene under its holder (made by the town's PlaceCar, or kept from the last build): prefab, paint and the two-box collider.</summary>
        public static GameObject PlaceParked(GameObject holder, Kind kind, Material paint)
        {
            var prefab = ParkedPrefab(kind);
            if (prefab == null) return null;
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                mats[0] = paint;
                r.sharedMaterials = mats;
            }
            var bounds = new Bounds(); bool any = false;
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>())
                if (r.name == "LOD0") { bounds = r.bounds; any = true; }
            if (any)
            {
                var local = new Bounds(holder.transform.InverseTransformPoint(bounds.center), new Vector3(prefabWidth(prefab), bounds.size.y, prefabLength(prefab)));
                Colliders(holder, local);
            }
            return body;
        }

        // ---- the hand-modelled pack cars, in the town's colours -----------------------------
        /// <summary>The hand-made models already in the project (his pick 2026-11-06): parked and in traffic beside our own. No working doors or seat, so not to drive. Their paint is one material each.</summary>
        static readonly (string prefab, int weight)[] PackCars = { ("Car_Sedan_A", 4), ("Car_Sedan_B", 3), ("Car_Sport", 2), ("Car_Touring", 2) };
        static readonly string[] PackPaintSlots = { "RMCar26_Paint", "Body1", "M_Carbody666" };

        static Material PackPaint(Material source, string colour)
        {
            string path = $"{Out}/PackPaint_{source.name}_{colour}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(source);
            var entry = PaintTable.First(p => p.name == colour);
            m.CopyPropertiesFromMaterial(source);
            m.SetColor("_BaseColor", entry.c);
            m.SetFloat("_Metallic", Mathf.Max(entry.metal, source.HasProperty("_Metallic") ? Mathf.Min(0.3f, source.GetFloat("_Metallic")) : 0f));
            m.SetFloat("_Smoothness", entry.smooth);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static void RepaintPack(GameObject body, string colour)
        {
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool any = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && System.Array.IndexOf(PackPaintSlots, mats[i].name) >= 0) { mats[i] = PackPaint(mats[i], colour); any = true; }
                if (any) r.sharedMaterials = mats;
            }
        }

        static string PickPack(System.Random rng)
        {
            int total = PackCars.Sum(k => k.weight), roll = rng.Next(total);
            foreach (var k in PackCars) { roll -= k.weight; if (roll < 0) return k.prefab; }
            return PackCars[0].prefab;
        }

        static bool PlacePack(GameObject holder, string pack, string colour)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FPSKitTownAssets.Out}/Parked/Parked_{pack}.prefab");
            if (prefab == null) { Debug.LogWarning($"[FPSKit] cars: no parked {pack}"); return false; }
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity;
            Bounds Size()
            {
                var b = new Bounds(); bool any = false;
                foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                return b;
            }
            // Laid along the holder and stood on its base, as the town's own placement does, with the holder squared for the measuring.
            var savedRot = holder.transform.rotation; holder.transform.rotation = Quaternion.identity;
            var w = Size();
            if (w.size.x > w.size.z * 1.15f) { body.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); w = Size(); }
            body.transform.position += new Vector3(holder.transform.position.x - w.center.x, holder.transform.position.y - w.min.y, holder.transform.position.z - w.center.z);
            w = Size();
            var local = new Bounds(holder.transform.InverseTransformPoint(w.center), w.size);
            holder.transform.rotation = savedRot;
            Colliders(holder, local);
            RepaintPack(body, colour);
            return true;
        }

        static GameObject[] PackTraffic()
        {
            var list = new List<GameObject>();
            foreach (var colour in new[] { "Sky", "White", "Maroon" })
            {
                string src = $"{FPSKitTownAssets.Out}/Traffic_Car_Sedan_A.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null) break;
                var root = PrefabUtility.LoadPrefabContents(src);
                RepaintPack(root, colour);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/Traffic_Pack_{colour}.prefab");
                PrefabUtility.UnloadPrefabContents(root);
                list.Add(saved);
            }
            return list.ToArray();
        }

        static float prefabWidth(GameObject prefab) => prefab.transform.Find("LOD0").GetComponent<MeshFilter>().sharedMesh.bounds.size.x;
        static float prefabLength(GameObject prefab) => prefab.transform.Find("LOD0").GetComponent<MeshFilter>().sharedMesh.bounds.size.z;

        public static Kind Pick(System.Random rng)
        {
            int total = Kinds.Sum(k => k.weight), roll = rng.Next(total);
            foreach (var k in Kinds) { roll -= k.weight; if (roll < 0) return k; }
            return Kinds[0];
        }

        /// <summary>What everything needs before any car is made: textures, materials and the sound of the dust. Prefabs for traffic.</summary>
        public static GameObject[] TrafficPrefabs()
        {
            MakeDustMaterial();
            return Kinds.Select(TrafficPrefab).Where(p => p != null).ToArray();
        }

        static void MakeDustMaterial()
        {
            Ensure("Assets/Resources/Car");
            string path = "Assets/Resources/Car/Dust.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            var puff = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (puff != null) m.SetTexture("_BaseMap", puff);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);

            // Tyre marks: black, a little see-through, on the road.
            string skidPath = "Assets/Resources/Car/Skid.mat";
            var skid = AssetDatabase.LoadAssetAtPath<Material>(skidPath);
            bool makeSkid = skid == null;
            if (makeSkid) skid = new Material(m);
            skid.CopyPropertiesFromMaterial(m);
            skid.SetTexture("_BaseMap", null);
            skid.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 0.42f));
            skid.renderQueue = (int)RenderQueue.Transparent - 10;
            if (makeSkid) AssetDatabase.CreateAsset(skid, skidPath); else EditorUtility.SetDirty(skid);
        }

        // ------------------------------------------------------------------ into the saved scene, without a rebuild
        const string DesertScene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        public static bool IsWreck(GameObject holder)
        {
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && (m.name.Contains("WreckHole") || m.name.Contains("BurntWreck") || m.name.Contains("RustBody"))) return true;
            return false;
        }

        /// <summary>
        /// Puts the new cars in the saved desert scene, its lighting and navmesh untouched: kerbside and yard cars swapped where they stand (wrecks stay: a burnt shell is a burnt shell),
        /// the drivable ones replaced on their spots, the traffic director's list and colours filled. <c>Tools/unity-batch.sh FPSKitBatch.PatchDesertCars</c>.
        /// </summary>
        public static void PatchDesert()
        {
            FPSKitSceneBuilder.MakeGlassMaterials();
            var traffic = TrafficPrefabs().Concat(PackTraffic()).ToArray();
            var scene = EditorSceneManager.OpenScene(DesertScene, OpenSceneMode.Single);
            var rng = new System.Random(20261106);

            int swapped = 0, left = 0;
            // Found first: what is inside them is destroyed as this goes.
            var everything = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).ToArray();
            var parkedGroups = everything.Where(t => t.name == "ParkedCars").ToArray();
            var driveGroups = everything.Where(t => t.name == "DrivableCars").ToArray();
            foreach (var t in parkedGroups)
                {
                    foreach (Transform holder in t.Cast<Transform>().ToArray())
                    {
                        bool oldSedan = holder.name.StartsWith("Car_Sedan") || holder.name.StartsWith("Parked_") || holder.name.StartsWith("Cars_");
                        if (!oldSedan || IsWreck(holder.gameObject)) { left++; continue; }
                        // A car stacked on another is junkyard: it is a wreck by this test too. What remains is at street level.
                        foreach (Transform child in holder.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                        foreach (var c in holder.GetComponents<Collider>()) Object.DestroyImmediate(c);
                        // About one car in three is a hand-made one, in any colour.
                        if (rng.NextDouble() < 0.34 && PlacePack(holder.gameObject, PickPack(rng), PaintTable[rng.Next(PaintTable.Length)].name))
                            holder.name = "Cars_Pack";
                        else
                        {
                            var kind = Pick(rng);
                            holder.name = "Cars_" + kind.name;
                            PlaceParked(holder.gameObject, kind, PaintFor(kind, rng));
                        }
                        foreach (var tr in holder.GetComponentsInChildren<Transform>(true))
                        {
                            tr.gameObject.layer = LayerMask.NameToLayer("Environment");
                            GameObjectUtility.SetStaticEditorFlags(tr.gameObject, 0);
                        }
                        swapped++;
                    }
                }

            int drivable = 0;
            foreach (var t in driveGroups)
                {
                    var spots = t.Cast<Transform>().Select(c => (c.position, c.eulerAngles.y)).ToArray();
                    foreach (Transform c in t.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
                    string[] kinds = { "Saloon", "Pickup", "OffRoader" };
                    string[] colours = { "Red", "Teal", "Orange" };
                    for (int i = 0; i < spots.Length; i++)
                    {
                        var kind = Kinds.First(k => k.name == kinds[i % kinds.Length]);
                        MakeDrivable(t, kind, Mats()["Paint" + colours[i % colours.Length]], spots[i].position, spots[i].y);
                        drivable++;
                    }
                }

            int directors = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var d in go.GetComponentsInChildren<TrafficDirector>(true))
                {
                    d.carPrefabs = traffic;
                    d.paints = AllPaints();
                    EditorUtility.SetDirty(d);
                    directors++;
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] cars: {swapped} parked swapped, {left} left as they were, {drivable} drivable, {directors} traffic directors ({traffic.Length} kinds)");
        }
    }
}
#endif
