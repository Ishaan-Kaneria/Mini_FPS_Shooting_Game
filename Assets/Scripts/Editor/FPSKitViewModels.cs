#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds the player's first-person models: the arms and kick legs from the Survivalist
    /// pack, and one prefab per store gun from Low Poly Weapons VOL.1, each with a
    /// <see cref="ViewModelSpec"/> that says where the hands go. Then stamps each prefab onto
    /// its <see cref="WeaponData.viewModel"/>, which is all the game needs: the rig is added
    /// at runtime (see <see cref="ViewModelRig"/>), so no arena scene is rebuilt.
    ///
    /// Everything it writes is under FPSKit_Generated/ViewModels and is regenerable. The
    /// packs are read, never changed: their materials are Built-in pipeline shaders, which
    /// draw pink in URP, so the guns get a URP Lit copy here and the bodies use the
    /// pack's own URP material set.
    ///
    ///   FPSKit > View Models > Build          (or FPSKitBatch.BuildViewModels)
    ///   FPSKit > View Models > Preview        renders the hands on each gun to Build/ViewModels
    /// </summary>
    public static class FPSKitViewModels
    {
        public const string Folder = "Assets/FPSKit_Generated/ViewModels";

        const string GunPack = "Assets/Low Poly Weapons VOL.1";
        const string BodySource = "Assets/Survivalist/Prefab/Survivalist (1).prefab";
        const string ArmsSource = "Assets/Survivalist/Prefab/FPS_Survivalist (1).prefab";
        const string BodyUrpMaterials = "Assets/Survivalist/Materials URP";

        /// <summary>
        /// One gun, measured in the pack model's own space turned to face +Z (the pack's
        /// guns look down -Z): where the line of sight runs, where the trigger and the
        /// handguard are, and which parts move. Read from the meshes and a side render.
        /// </summary>
        class Gun
        {
            public string pack;               // prefab name in the pack
            public string weaponData;         // asset path of the WeaponData it is for
            public float sightX, sightY;      // the line of sight
            public float rearZ;               // where the rear sight is along the barrel
            public Vector3 trigger;           // centre of the trigger
            public Vector3 handguard;         // where the left palm holds, on the underside
            public string magazine;           // child that drops on a reload
            public string bolt;               // child that runs on each shot
            public Vector3 boltTravel = new Vector3(0f, 0f, -0.04f);
            public Vector3 hip = new Vector3(0.16f, -0.095f, 0.3f);
            public Vector3 aim = new Vector3(0f, 0f, 0.22f);
        }

        static readonly Gun[] Guns =
        {
            new Gun { pack = "M4_8", weaponData = "Assets/FPSKit_Generated/TestRifle.asset",
                      sightY = 0.104f, rearZ = -0.10f, trigger = new Vector3(0f, 0.002f, -0.096f),
                      handguard = new Vector3(0f, 0.033f, 0.15f), magazine = "Mag", bolt = "Bolt",
                      boltTravel = new Vector3(0f, 0f, -0.03f) },
            new Gun { pack = "AK74", weaponData = "Assets/FPSKit_Generated/Store/Gun_Burst.asset",
                      sightY = 0.046f, rearZ = 0.05f, trigger = new Vector3(0f, -0.04f, -0.049f),
                      handguard = new Vector3(0f, -0.01f, 0.23f), magazine = "Mag", bolt = "Bolt",
                      boltTravel = new Vector3(0f, 0f, -0.07f) },
            new Gun { pack = "Uzi", weaponData = "Assets/FPSKit_Generated/Store/Gun_SMG.asset",
                      sightY = 0.06f, rearZ = -0.03f, trigger = new Vector3(0f, -0.001f, 0.017f),
                      handguard = new Vector3(0f, 0.011f, 0.11f), magazine = "Magazin", bolt = "Bolt",
                      boltTravel = new Vector3(0f, 0f, -0.05f), hip = new Vector3(0.15f, -0.085f, 0.26f) },
            new Gun { pack = "Bennelli_M4", weaponData = "Assets/FPSKit_Generated/Store/Gun_Shotgun.asset",
                      sightY = 0.036f, rearZ = -0.15f, trigger = new Vector3(0f, -0.046f, -0.137f),
                      handguard = new Vector3(0f, -0.05f, 0.09f), magazine = null, bolt = "Pump",
                      boltTravel = new Vector3(0f, 0f, -0.09f) },
            new Gun { pack = "M107", weaponData = "Assets/FPSKit_Generated/Store/Gun_DMR.asset",
                      sightY = 0.09f, rearZ = -0.10f, trigger = new Vector3(0f, -0.035f, -0.128f),
                      handguard = new Vector3(0f, 0.0f, 0.18f), magazine = "Magazin", bolt = "Bolt",
                      boltTravel = new Vector3(0f, 0f, -0.06f) },
            new Gun { pack = "M249", weaponData = "Assets/FPSKit_Generated/Store/Gun_LMG.asset",
                      sightX = -0.024f, sightY = 0.112f, rearZ = -0.2f, trigger = new Vector3(-0.021f, -0.008f, -0.107f),
                      handguard = new Vector3(-0.024f, -0.005f, 0.14f), magazine = "Magazin", bolt = "Bolt",
                      boltTravel = new Vector3(0f, 0f, -0.06f) },
        };

        /// <summary>
        /// Where the wrists sit and which way the hands face, relative to the trigger and
        /// the handguard, in the gun's space (+Z down the barrel, +Y up, +X right). The
        /// frames are "fingers" (wrist to knuckles) and "back" (the back of the hand).
        /// </summary>
        static readonly Vector3 RightWristFromTrigger = new Vector3(0.03f, -0.015f, -0.12f);
        static readonly Vector3 RightFingers = new Vector3(-0.3f, -0.2f, 0.93f);
        static readonly Vector3 RightBack = new Vector3(0.95f, 0.15f, 0.25f);
        static readonly Vector3 LeftWristFromHandguard = new Vector3(-0.02f, -0.055f, -0.08f);
        static readonly Vector3 LeftFingers = new Vector3(0.35f, 0.15f, 0.92f);
        static readonly Vector3 LeftBack = new Vector3(-0.45f, -0.88f, 0.05f);

        /// <summary>
        /// Where the shoulders sit, from head-on-the-eye: down, so the stock is in the
        /// shoulder pocket below the cheek and the arms bend the way a held rifle bends them.
        /// </summary>
        static readonly Vector3 ArmsOffset = new Vector3(0f, -0.06f, 0.007f);

        // ==================================================================

        [MenuItem("FPSKit/View Models/Build", false, 64)]
        public static void Build()
        {
            EnsureFolder(Folder);

            // The gloved arms and the sleeve only: the pack's bare-armed mesh lies under the gloved one,
            // and with both drawn the skin showed through the gloves in white patches.
            bool ArmPart(string n) => n.Contains("FPS") && (n.Contains("Gloves") || n.Contains("Shirt"));
            var arms = BuildBody(ArmsSource, "FP_Arms", keep: ArmPart, enable: ArmPart);
            // The player's own body: what is seen looking down, and the leg that kicks. No
            // head and no arms (the first-person arms are separate); one vest.
            bool BodyPart(string n) => n.Contains("Pants") || n.Contains("Boots") || n == "SK_Military_Shirt3" || n == "SK_Military_Vest1";
            var legs = BuildBody(BodySource, "FP_Body", keep: BodyPart, enable: BodyPart);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/FP_Legs.prefab") != null)
                AssetDatabase.DeleteAsset(Folder + "/FP_Legs.prefab");
            var material = GunMaterial();

            int stamped = 0;
            foreach (var g in Guns)
            {
                var prefab = BuildGun(g, material, arms, legs);
                if (prefab == null) continue;

                var data = AssetDatabase.LoadAssetAtPath<WeaponData>(g.weaponData);
                if (data == null) { Debug.LogWarning($"[FPSKit] View Models: no WeaponData at {g.weaponData}"); continue; }

                if (data.viewModel != prefab)
                {
                    data.viewModel = prefab;
                    EditorUtility.SetDirty(data);
                }
                stamped++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] View Models: arms {(arms != null ? "ok" : "MISSING")}, legs {(legs != null ? "ok" : "MISSING")}, {stamped}/{Guns.Length} guns stamped");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>The guns' palette texture on URP Lit. Matte: a low-poly gun with a mirror finish reads as plastic.</summary>
        static Material GunMaterial()
        {
            string path = Folder + "/Gun_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GunPack + "/Textures_Guns.png");
            mat.SetTexture("_BaseMap", tex);
            // Darkened: the pack's palette is a bright tan and orange that reads as a toy.
            mat.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.58f));
            mat.SetFloat("_Smoothness", 0.28f);
            mat.SetFloat("_Metallic", 0.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// A Survivalist body cut down to the meshes asked for, on its full skeleton, with
        /// the pack's URP materials in place of the Built-in ones it ships with.
        /// </summary>
        static GameObject BuildBody(string source, string name, System.Func<string, bool> keep, System.Func<string, bool> enable)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (src == null) { Debug.LogWarning($"[FPSKit] View Models: {source} is missing"); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = name;
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;

                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!keep(smr.name)) { Object.DestroyImmediate(smr.gameObject); continue; }
                    if (enable(smr.name)) smr.gameObject.SetActive(true);
                    if (!smr.gameObject.activeSelf) { Object.DestroyImmediate(smr.gameObject); continue; }

                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = UrpMaterial(mats[i]);
                    smr.sharedMaterials = mats;
                }

                // Parents left with nothing under them (the FPS arms' group in the legs).
                foreach (Transform child in new List<Transform>(Children(go.transform)))
                    if (child.GetComponentInChildren<Renderer>(true) == null && child.name != "root")
                        Object.DestroyImmediate(child.gameObject);

                var animator = go.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                string path = $"{Folder}/{name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static IEnumerable<Transform> Children(Transform t)
        {
            foreach (Transform c in t) yield return c;
        }

        /// <summary>
        /// The pack's URP twin of a Built-in material, by name, in the outfit asked for: the
        /// pack ships each garment in up to four skins (Skin1 is blue jeans and a grey shirt;
        /// Skin2 is olive drab). The original when there is none.
        /// </summary>
        static Material UrpMaterial(Material m, string skin = "Skin2")
        {
            if (m == null) return null;
            // The gloves stay dark (Skin1, black tactical): the olive outfit's gloves are a tan camo
            // that lit up white and gold in first person.
            string name = m.name.Contains("Gloves") ? m.name.Replace("Skin2", "Skin1") : m.name.Replace("Skin1", skin);
            var urp = AssetDatabase.LoadAssetAtPath<Material>($"{BodyUrpMaterials}/URP_{name}.mat");
            if (urp == null) urp = AssetDatabase.LoadAssetAtPath<Material>($"{BodyUrpMaterials}/URP_{m.name}.mat");
            if (urp == null) Debug.LogWarning($"[FPSKit] View Models: no URP version of {m.name}; it will draw pink");
            return urp != null ? urp : m;
        }

        /// <summary>
        /// One gun: the pack model turned to look down +Z with its rear sight on the origin,
        /// the moving parts put under pivots that share the root's axes (so the reload's
        /// drop is in the same space as the rest of the gun), and the hand sockets.
        /// </summary>
        static GameObject BuildGun(Gun g, Material material, GameObject arms, GameObject legs)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{GunPack}/Prefabs/{g.pack}.prefab");
            if (src == null) { Debug.LogWarning($"[FPSKit] View Models: {g.pack} is missing"); return null; }

            var root = new GameObject("VM_" + g.pack);
            try
            {
                var spec = root.AddComponent<ViewModelSpec>();

                var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                // The gun's own coordinates (turned) put the sight line through the origin.
                Vector3 origin = new Vector3(g.sightX, g.sightY, g.rearZ);
                model.transform.localPosition = -origin;

                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }

                spec.magazine = Pivot(root.transform, model.transform, g.magazine, "MagazinePivot");
                spec.bolt = Pivot(root.transform, model.transform, g.bolt, "BoltPivot");
                spec.boltTravel = g.boltTravel;

                Vector3 trigger = g.trigger - origin;
                Vector3 handguard = g.handguard - origin;

                spec.rightHand = Socket(root.transform, "RightHand", trigger + RightWristFromTrigger, RightFingers, RightBack);
                spec.leftHand = Socket(root.transform, "LeftHand", handguard + LeftWristFromHandguard, LeftFingers, LeftBack);

                // The end of the barrel, measured: the furthest point forward, at the mean
                // height of what is out there.
                spec.muzzle = new GameObject("Muzzle").transform;
                spec.muzzle.SetParent(root.transform, false);
                spec.muzzle.localPosition = MuzzleOf(root.transform, model.transform);

                spec.hipPosition = g.hip;
                spec.buttLength = Mathf.Max(0.1f, -RearOf(root.transform, model.transform));
                spec.armsOffset = ArmsOffset;
                spec.aimPosition = g.aim;
                spec.armsPrefab = arms;
                spec.legsPrefab = legs;

                return PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/VM_{g.pack}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Puts a moving part under an empty at its centre whose axes are the root's, and
        /// returns the empty. Null when the gun has no such part.
        /// </summary>
        static Transform Pivot(Transform root, Transform model, string part, string name)
        {
            if (string.IsNullOrEmpty(part)) return null;

            Transform found = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == part) { found = t; break; }
            if (found == null) return null;

            var r = found.GetComponent<Renderer>();
            Vector3 centre = r != null ? r.bounds.center : found.position;

            var pivot = new GameObject(name).transform;
            pivot.SetParent(root, false);
            pivot.position = centre;
            found.SetParent(pivot, true);
            return pivot;
        }

        static Transform Socket(Transform root, string name, Vector3 position, Vector3 fingers, Vector3 back)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = position;
            t.localRotation = Quaternion.LookRotation(fingers.normalized, back.normalized);
            return t;
        }

        /// <summary>The rearmost point of the gun along the barrel, in the root's space (negative: behind the sight).</summary>
        static float RearOf(Transform root, Transform model)
        {
            float minZ = 0f;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                    minZ = Mathf.Min(minZ, root.InverseTransformPoint(mf.transform.TransformPoint(v)).z);
            }
            return minZ;
        }

        static Vector3 MuzzleOf(Transform root, Transform model)
        {
            float maxZ = float.MinValue;
            var points = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(v));
                    points.Add(p);
                    maxZ = Mathf.Max(maxZ, p.z);
                }
            }

            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in points)
                if (p.z > maxZ - 0.015f) { sum += p; n++; }

            return n > 0 ? new Vector3(sum.x / n, sum.y / n, maxZ) : new Vector3(0f, 0f, 0.5f);
        }

        // ==================================================================
        // Preview: the hands on each gun, at rest, aimed and through the strikes.
        // ==================================================================

        [MenuItem("FPSKit/View Models/Preview", false, 65)]
        public static void PreviewMenu() => Preview("Build/ViewModels", null, 640, 360);

        /// <summary>
        /// Poses a rig in a scratch scene and renders the eye's view for each requested
        /// state. Edit mode, nothing saved. Returns the files written.
        /// </summary>
        public static List<string> Preview(string folder, string onlyGun, int width, int height, string states = "hip,aim,reload,jab,cross,kick,takedown", string views = "eye")
        {
            var written = new List<string>();
            Directory.CreateDirectory(folder);

            var previous = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;

            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);

                var far = new Vector3(0f, 5000f, 0f);

                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

                var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
                backdrop.transform.position = far + new Vector3(0f, 0f, 6f);
                backdrop.transform.localScale = new Vector3(30f, 20f, 1f);
                var bmat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                bmat.SetColor("_BaseColor", new Color(0.36f, 0.4f, 0.44f));
                backdrop.GetComponent<Renderer>().sharedMaterial = bmat;

                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.position = far + new Vector3(0f, -1.65f, 0f);
                var fmat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                fmat.SetColor("_BaseColor", new Color(0.3f, 0.29f, 0.26f));
                floor.GetComponent<Renderer>().sharedMaterial = fmat;

                var body = new GameObject("Player").transform;
                body.position = far + new Vector3(0f, -1.65f, 0f);

                var camGo = new GameObject("Eye");
                camGo.transform.SetParent(body, false);
                camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 62f;      // the game's 75 horizontal-ish at 16:9, as a vertical FOV
                cam.nearClipPlane = 0.02f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.3f, 0.33f, 0.36f);

                var holder = new GameObject("WeaponHolder").transform;
                holder.SetParent(camGo.transform, false);

                foreach (var g in Guns)
                {
                    if (onlyGun != null && g.pack != onlyGun) continue;

                    var vm = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/VM_{g.pack}.prefab");
                    if (vm == null) continue;

                    var rigGo = new GameObject("Rig");
                    rigGo.transform.SetParent(holder, false);
                    var rig = rigGo.AddComponent<ViewModelRig>();
                    rig.PreviewBody = body;
                    rig.Build(vm, holder, 0);

                    var markers = new List<GameObject>();
                    foreach (string state in states.Split(','))
                    {
                        var s = StateFor(state.Trim());
                        string st = state.Trim();
                        float pitch = st.StartsWith("kick") ? 12f : st == "down" || st == "walk" ? 65f : 0f;
                        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                        rig.Pose(s);

                        // Skinned meshes are skinned once per editor frame, and every render
                        // here is in the same frame: without this, each state would show the
                        // first state's arms. Baked into plain meshes for the shot instead.
                        var baked = Bake(body.parent != null ? body.parent : body);

                        foreach (string view in views.Split(','))
                        {
                            // A second camera looking at the rig from outside, with the
                            // sockets marked: red the right wrist, blue the left, the
                            // short stick the way the fingers point.
                            Camera shot = cam;
                            Camera outside = null;
                            if (view != "eye")
                            {
                                if (markers.Count == 0) markers = Markers(rig);
                                outside = new GameObject("Outside").AddComponent<Camera>();
                                outside.clearFlags = CameraClearFlags.SolidColor;
                                outside.backgroundColor = new Color(0.3f, 0.33f, 0.36f);
                                outside.nearClipPlane = 0.01f;
                                outside.fieldOfView = 40f;
                                Vector3 eye = camGo.transform.position;
                                Vector3 at = eye + body.forward * 0.3f + Vector3.down * 0.15f;
                                var spec = holder.GetComponentInChildren<ViewModelSpec>();
                                if (view == "rhand" || view == "lhand")
                                {
                                    // Close on one wrist, from that hand's own outside and a little below.
                                    var sock = view == "rhand" ? spec.rightHand : spec.leftHand;
                                    at = sock.position + sock.forward * 0.05f;
                                    Vector3 side = view == "rhand" ? body.right : -body.right;
                                    outside.transform.position = at + side * 0.45f + Vector3.down * 0.12f - body.forward * 0.05f;
                                    outside.fieldOfView = 30f;
                                }
                                else
                                {
                                    outside.transform.position = view == "side" ? at + body.right * 1.3f
                                                               : view == "top" ? at + Vector3.up * 1.3f + body.forward * 0.01f
                                                               : at + body.forward * 1.4f + Vector3.up * 0.2f;
                                }
                                outside.transform.LookAt(at, view == "top" ? body.forward : Vector3.up);
                                shot = outside;
                            }

                            foreach (var m in markers) m.SetActive(view != "eye");
                            string file = $"{folder}/{g.pack}_{state.Trim()}_{view}.png";
                            written.Add(FairgroundShots.Capture(shot, file, width, height));
                            if (outside != null) Object.DestroyImmediate(outside.gameObject);
                        }
                        Unbake();
                    }
                    foreach (var m in markers) Object.DestroyImmediate(m);
                    Unbake();

                    rig.Clear();
                    Object.DestroyImmediate(rigGo);
                }
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }

            return written;
        }

        static readonly List<(SkinnedMeshRenderer smr, GameObject copy)> _baked = new List<(SkinnedMeshRenderer, GameObject)>();

        /// <summary>Every skinned mesh in the scratch scene, as it stands now, as a plain mesh.</summary>
        static int Bake(Transform root)
        {
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                var mesh = new Mesh();
                // Baked unscaled and the scale put on the copy: baking a scaled rig (the arms
                // are drawn a size up) with the scale in came out shifted.
                smr.BakeMesh(mesh, false);
                var copy = new GameObject(smr.name + "_Baked");
                copy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);

                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
                _baked.Add((smr, copy));
            }
            return _baked.Count;
        }

        static void Unbake()
        {
            foreach (var (smr, copy) in _baked)
            {
                if (smr != null) smr.enabled = true;
                if (copy != null)
                {
                    Object.DestroyImmediate(copy.GetComponent<MeshFilter>().sharedMesh);
                    Object.DestroyImmediate(copy);
                }
            }
            _baked.Clear();
        }

        /// <summary>Little coloured sticks on the hand sockets, for the outside views.</summary>
        static List<GameObject> Markers(ViewModelRig rig)
        {
            var list = new List<GameObject>();
            var spec = rig.GetComponentInParent<Transform>().parent.GetComponentInChildren<ViewModelSpec>();
            if (spec == null) return list;

            void Mark(Transform t, Color c)
            {
                if (t == null) return;
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", c);
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.SetParent(t, false);
                ball.transform.localScale = Vector3.one * 0.025f;
                ball.GetComponent<Renderer>().sharedMaterial = mat;
                var stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stick.transform.SetParent(t, false);
                stick.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                stick.transform.localScale = new Vector3(0.006f, 0.006f, 0.1f);
                stick.GetComponent<Renderer>().sharedMaterial = mat;
                var up = GameObject.CreatePrimitive(PrimitiveType.Cube);
                up.transform.SetParent(t, false);
                up.transform.localPosition = new Vector3(0f, 0.025f, 0f);
                up.transform.localScale = new Vector3(0.004f, 0.05f, 0.004f);
                up.GetComponent<Renderer>().sharedMaterial = mat;
                list.Add(ball); list.Add(stick); list.Add(up);
            }

            Mark(spec.rightHand, Color.red);
            Mark(spec.leftHand, Color.blue);
            Mark(spec.muzzle, Color.yellow);
            return list;
        }

        static ViewModelRig.State StateFor(string name)
        {
            var s = new ViewModelRig.State { strike = MeleeStrike.Strike.None, strikeProgress = -1f, reloadProgress = -1f, sinceShot = 9f };
            switch (name)
            {
                case "aim": s.aim = 1f; break;
                case "reload": s.reloadProgress = 0.45f; s.reloadEmpty = true; break;
                case "jab": s.fistsUp = 1f; s.gunAway = 1f; s.strike = MeleeStrike.Strike.Jab; s.strikeProgress = 0.36f; break;
                case "guard": s.fistsUp = 1f; s.gunAway = 1f; break;
                case "holster": s.gunAway = 0.55f; s.fistsUp = 0.3f; break;
                case "cross": s.fistsUp = 1f; s.gunAway = 1f; s.strike = MeleeStrike.Strike.Cross; s.strikeProgress = 0.4f; break;
                case "kick": s.strike = MeleeStrike.Strike.Kick; s.strikeProgress = 0.5f; break;
                case "kickchamber": s.strike = MeleeStrike.Strike.Kick; s.strikeProgress = 0.2f; break;
                case "walk": s.velocity = new Vector3(0f, 0f, 3f); s.lookDown = 0.9f; break;
                case "down": s.lookDown = 0.9f; break;
                case "takedown": s.strike = MeleeStrike.Strike.Takedown; s.strikeProgress = 0.38f; break;
            }
            return s;
        }
    }
}
#endif
