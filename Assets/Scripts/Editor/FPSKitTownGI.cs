#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Real bounced light for the desert town: Adaptive Probe Volumes (Ishaan, 2026-10-24: "make the shadows, lighting very
    /// realistic. Forget about the size, I want real"; Docs/DESIGN_NOTES.md "Real light: adaptive probe volumes").
    ///
    /// Until now the town's walls took the flat sky ambient whole, one colour indoors and out: a room was lit like the street
    /// with the sun taken away -- no light falling in from the windows, no darker far wall, nothing under a table -- and the
    /// light probes (FPSKitTownProbes) lit only what moves. APV fills the town with probes, dense wherever there is
    /// geometry (3 m: Unity's limit is 67 M probes a set, and at 1 m the town wanted more), baked by path tracing from the
    /// sky and the sun's bounce, and every pixel of every renderer -- wall, shelf,
    /// face, car -- reads the light where it is. No lightmap UVs, so it works on the generated meshes.
    ///
    /// - High and Medium use APV (their pipeline assets: FPSKitQualityTiers). Low -- the phones and the web build -- keeps the
    ///   classic probes, so the scene carries both: the classic bake first (BakeTownProbes), then this, APV only.
    /// - Every static mesh renderer in the arena contributes and takes its light from probes, but for the few insides with a
    ///   lightmap (FPSKitTownLightmaps, the interior lighting pilot); what moves (people, cars, enemies) only takes it.
    /// - Baked on the GPU when there is a graphics device (UNITY_GRAPHICS=1), else on the CPU (slow).
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.BakeDesertLight      (the saved scene, without rebuilding it)
    /// </summary>
    public static class FPSKitTownGI
    {
        public const string DesertScene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        /// <summary>
        /// Bounce strength for the APV bake: the classic probes' boost (FPSKitTownProbes) made up for walls lit by the whole
        /// sky ambient beside people lit by probes; with everything on probes the light can be what it is. Tried at 1.0 once
        /// the ground had its real albedo (the realism pass, step 1, 2026-10-29): every shaded wall lost about a quarter of
        /// its brightness on screen (lighting tour, 150 to 110), the soldier in face_shade went nearly black and rooms
        /// 5-34 %; the brighter street does not make it up, a shaded wall being lit by the sky and the other walls. 1.8 kept.
        /// </summary>
        internal const float ApvIndirect = 1.8f;

        /// <summary>
        /// The sky's share of the bake against the scene's own ambient strength. With everything on probes a shaded street
        /// is lit by the sky it can see and the light off the sunlit sand and walls, as it should be, but at 1.15 bounce and
        /// the plain sky a man in a building's shade read darker than the old flat ambient left him (the lighting tour,
        /// face_shade): his face was the thing asked to be more visible. 1.8 and 1.3.
        /// </summary>
        internal const float ApvSky = 1.3f;

        /// <summary>Metres the probe volume spans across the town (the arena is a kilometre; a little over for its edge).</summary>
        const float TownSpan = 1060f;

        /// <summary>
        /// Probes past which the bake is stopped before it starts lighting them (FPSKIT_GI_MAX_PROBES): the editor ran out of
        /// memory baking the whole terrain's worth.
        /// </summary>
        static long MaxProbes => long.TryParse(Environment.GetEnvironmentVariable("FPSKIT_GI_MAX_PROBES"), out var n) ? n : 4_000_000;

        /// <summary>
        /// The scene's own lights made honest now the light that reaches the shade is baked: the sun's shadows at full
        /// strength (0.8 let a fifth of the sun through every wall and awning), and the shadowless "Bounce Light"
        /// (FPSKitSceneBuilder, 2026-10-03: a weak directional light from the far side, standing in for the sand's bounce)
        /// switched off -- it lit every shaded face and every room by the same amount, through the walls, and flattened the
        /// interiors. And the eye that adapts to the dark indoors (EyeAdaptation). The reduced copy (phones, the web) puts the
    /// lights back and removes the eye: it has no probe volumes.
        /// </summary>
        public static void ApplyRealLightLook()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                if (l.name == "Bounce Light") { l.enabled = false; continue; }
                if (l.shadows != LightShadows.None) l.shadowStrength = 1f;
            }
            // Rooms as dark against the street as they are want the eye that adapts to them (EyeAdaptation). Made afresh each
            // time, so it carries the code's values (a component saved with the scene keeps the values it was saved with).
            foreach (var old in Object.FindObjectsByType<EyeAdaptation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("EyeAdaptation");
            var arena = GameObject.Find("Arena");
            if (arena != null) go.transform.SetParent(arena.transform, false);
            go.AddComponent<EyeAdaptation>();
            PlaceIndoorReflections(arena != null ? arena.transform : null);
        }

        const string RoomCubePath = "Assets/FPSKit_Generated/Town/IndoorRoom.cubemap";

        /// <summary>
        /// A box reflection probe round every house with an inside (FPSKitTownInterior's room marks) and every shop with one
        /// (FPSKitTownShopfront's marks), each showing the same small warm room cubemap; switched on near the eye under
        /// Forward+ by <see cref="IndoorReflections"/>. A house's box is the union of its rooms, each found by casting from
        /// its middle to the walls and the ceiling above head height (clear of furniture); a shop's is its inside as built,
        /// from 6 cm behind the glass (the glass reflects the street) to the back wall.
        ///
        /// URP fades a probe in from its box's faces inward over the blend distance (ReflectionProbe.bounds is the box
        /// alone), so a box drawn on the room's surfaces gave them almost nothing: the first house probes had the floor
        /// 5 cm inside a 0.25 m fade (a fifth of the room, four fifths sky) and the walls on the faces themselves. Each box
        /// now reaches <see cref="RoomPad"/> past the floor, the walls and the ceiling, into their thickness (a house wall
        /// is 0.25 m), and fades over <see cref="RoomBlend"/>. Replaced whole each time.
        /// </summary>
        static void PlaceIndoorReflections(Transform arena)
        {
            foreach (var old in Object.FindObjectsByType<IndoorReflections>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var rooms = GameObject.Find("HouseRooms");
            var shops = GameObject.Find("ShopRooms");
            if (rooms == null && shops == null) { Debug.LogWarning("[FPSKit] indoor reflections: no HouseRooms or ShopRooms in the scene"); return; }
            var cube = RoomCube();
            var boxes = new List<Bounds>();
            var root = new GameObject("IndoorReflections");
            if (arena != null) root.transform.SetParent(arena, false);
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian", "PlayerBounds");
            Physics.SyncTransforms();
            void Probe(string name, Bounds box, float intensity)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = box.center;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = cube;
                probe.size = box.size;
                probe.center = Vector3.zero;
                probe.boxProjection = false;
                probe.blendDistance = RoomBlend;
                probe.importance = 2;                // over the street probes where they overlap
                probe.intensity = intensity;
                probe.enabled = false;               // IndoorReflections switches them on near the eye
                boxes.Add(box);
            }
            int houses = 0;
            if (rooms != null)
            foreach (Transform house in rooms.transform)
            {
                bool any = false;
                var box = new Bounds();
                foreach (Transform room in house)
                {
                    if (!room.name.StartsWith("Room ")) continue;
                    var look = room.Find("Look");
                    Vector3 floor = (look != null ? look.position : room.position) - Vector3.up * 0.3f;
                    Vector3 from = floor + Vector3.up * 2.1f;   // over the wardrobes, under the ceiling
                    float Cast(Vector3 d, float far) => Physics.Raycast(from, d, out var hit, far, mask, QueryTriggerInteraction.Ignore) ? hit.distance : -1f;
                    float xp = Cast(Vector3.right, 14f), xn = Cast(Vector3.left, 14f), zp = Cast(Vector3.forward, 14f), zn = Cast(Vector3.back, 14f);
                    float up = Physics.Raycast(floor + Vector3.up * 0.5f, Vector3.up, out var ceil, 6f, mask, QueryTriggerInteraction.Ignore) ? ceil.distance + 0.5f : -1f;
                    if (xp < 0f || xn < 0f || zp < 0f || zn < 0f || up < 0f) continue;   // open to the sky one way: not a room
                    var rb = new Bounds();
                    rb.SetMinMax(new Vector3(from.x - xn, floor.y - 0.05f, from.z - zn), new Vector3(from.x + xp, floor.y + up, from.z + zp));
                    if (!any) { box = rb; any = true; } else box.Encapsulate(rb);
                }
                if (!any) continue;
                box.Expand(2f * RoomPad);
                Probe("RoomProbe_" + house.GetSiblingIndex(), box, 1f);
                houses++;
            }
            // A shop: its mark is its inside as a unit box (FPSKitTownShopfront); the probe's box is world-aligned, as the
            // town's faces are. A dark shop (shut half way, or trading unlit) reflects a dimmer room.
            int lit = 0, dark = 0;
            if (shops != null)
            foreach (Transform shop in shops.transform)
            {
                // In the mark's units: out past the side walls, the floor, the ceiling and the back wall; the front 6 cm
                // behind the glass.
                var size = shop.localScale;
                float sx = 0.5f + RoomPad / size.x, sy = 0.5f + RoomPad / size.y, back = -0.5f - RoomPad / size.z, front = 0.5f - 0.06f / size.z;
                var box = new Bounds(shop.position, Vector3.zero);
                for (int c = 0; c < 8; c++)
                    box.Encapsulate(shop.TransformPoint(new Vector3((c & 1) != 0 ? sx : -sx, (c & 2) != 0 ? sy : -sy, (c & 4) != 0 ? front : back)));
                bool isDark = shop.name.EndsWith(" dark");
                Probe("ShopProbe_" + shop.GetSiblingIndex(), box, isDark ? ShopDarkReflection : 1f);
                if (isDark) dark++; else lit++;
            }
            root.AddComponent<IndoorReflections>();
            Debug.Log($"[FPSKit] indoor reflections: {houses} house probes, {lit + dark} shop probes ({dark} dark)");
            PlaceRoomContents(arena, boxes);
        }

        /// <summary>
        /// What is in each room (the buildings' props whose middle is in its box: furniture, shop goods), handed to a
        /// <see cref="RoomContents"/> that draws it only near the room. The same boxes as the rooms' reflection probes,
        /// but its own object: the phone and web copy removes the probes and keeps this. Replaced whole each time.
        /// </summary>
        static void PlaceRoomContents(Transform arena, List<Bounds> boxes)
        {
            foreach (var old in Object.FindObjectsByType<RoomContents>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var props = GameObject.Find("Arena/Buildings/BuildingProps");
            if (props == null || boxes.Count == 0) { Debug.LogWarning("[FPSKit] room contents: no BuildingProps or no rooms"); return; }
            // The rooms by 16 m cells of the ground, so each prop is tested against the few rooms round it.
            const float Cell = 16f;
            var cells = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                for (int x = Mathf.FloorToInt(b.min.x / Cell); x <= Mathf.FloorToInt(b.max.x / Cell); x++)
                    for (int z = Mathf.FloorToInt(b.min.z / Cell); z <= Mathf.FloorToInt(b.max.z / Cell); z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                        list.Add(i);
                    }
            }
            int Room(Vector3 c)
            {
                if (cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell)), out var near))
                    foreach (int i in near) if (boxes[i].Contains(c)) return i;
                return -1;
            }
            (int[] first, Renderer[] all) Listed(List<List<Renderer>> per)
            {
                var starts = new int[boxes.Count];
                var list = new List<Renderer>();
                for (int i = 0; i < boxes.Count; i++) { starts[i] = list.Count; list.AddRange(per[i]); }
                return (starts, list.ToArray());
            }
            var inRoom = boxes.Select(_ => new List<Renderer>()).ToList();
            int outside = 0;
            foreach (var r in props.GetComponentsInChildren<Renderer>(true))
            {
                int i = Room(r.bounds.center);
                if (i >= 0) inRoom[i].Add(r); else outside++;
            }
            // The houses' insides (FPSKitTownInterior.BuildHouseInside), each under its house's box.
            var shellOf = boxes.Select(_ => new List<Renderer>()).ToList();
            int shellsLost = 0;
            var insides = GameObject.Find("Arena/Buildings/HouseInteriors");
            if (insides != null)
                foreach (var r in insides.GetComponentsInChildren<Renderer>(true))
                {
                    int i = Room(r.bounds.center);
                    if (i >= 0) shellOf[i].Add(r); else shellsLost++;
                }
            var (first, all) = Listed(inRoom);
            var (shellFirst, shells) = Listed(shellOf);
            var go = new GameObject("RoomContents");
            if (arena != null) go.transform.SetParent(arena, false);
            go.AddComponent<RoomContents>().Set(boxes.ToArray(), first, all, shellFirst, shells);
            Debug.Log($"[FPSKit] room contents: {all.Length} renderers in {boxes.Count} rooms ({inRoom.Count(l => l.Count > 0)} with something in them), {outside} props outside any room; " +
                      $"{shells.Length} house insides placed, {shellsLost} outside every box (always drawn)");
        }

        /// <summary>How far a room's probe box reaches past its surfaces, into the walls, floor and ceiling.</summary>
        const float RoomPad = 0.1f;

        /// <summary>How far in from its box's faces a room's probe fades in; less than <see cref="RoomPad"/>, so every surface
        /// of the room takes the whole of it.</summary>
        const float RoomBlend = 0.05f;

        /// <summary>How bright the room a dark shop reflects is against a lit one's: its walls are the dark interior paint
        /// (0.16 against 0.34).</summary>
        const float ShopDarkReflection = 0.45f;

        /// <summary>
        /// The room every indoor surface reflects: a lit ceiling overhead, pale walls round, a darker floor below, warm, at about
        /// the light a room in this town has (linear values; the sky's reflection is several times brighter). 32 px a face.
        /// </summary>
        static Cubemap RoomCube()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(RoomCubePath);
            const int n = 32;
            var cube = existing != null ? existing : new Cubemap(n, TextureFormat.RGBAHalf, true);
            Color ceiling = new Color(0.36f, 0.33f, 0.29f), wall = new Color(0.26f, 0.23f, 0.19f), floor = new Color(0.09f, 0.075f, 0.06f);
            var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            var px = new Color[n * n];
            foreach (var face in faces)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        Vector3 d = face switch
                        {
                            CubemapFace.PositiveX => new Vector3(1f, -v, -u),
                            CubemapFace.NegativeX => new Vector3(-1f, -v, u),
                            CubemapFace.PositiveY => new Vector3(u, 1f, v),
                            CubemapFace.NegativeY => new Vector3(u, -1f, -v),
                            CubemapFace.PositiveZ => new Vector3(u, -v, 1f),
                            _ => new Vector3(-u, -v, -1f),
                        };
                        float h = d.normalized.y;
                        px[y * n + x] = h >= 0f ? Color.Lerp(wall, ceiling, Mathf.SmoothStep(0f, 1f, h)) : Color.Lerp(wall, floor, Mathf.SmoothStep(0f, 1f, -h));
                    }
                cube.SetPixels(px, face);
            }
            cube.Apply(true);
            if (existing == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RoomCubePath));
                AssetDatabase.CreateAsset(cube, RoomCubePath);
            }
            else EditorUtility.SetDirty(cube);
            return cube;
        }

        /// <summary>Opens the saved desert scene, applies <see cref="ApplyRealLightLook"/>, saves it, and exits (batch).</summary>
        public static void BatchLookSaved()
        {
            var scene = EditorSceneManager.OpenScene(DesertScene, OpenSceneMode.Single);
            FPSKitQualityTiers.Ensure();   // the tiers' shadows and SSAO as the code has them
            ApplyRealLightLook();
            FPSKitTownLightmaps.CaptureReflections(DesertScene);   // the indoor probes were placed afresh
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DesertScene);
            FPSKitBatch.Exit(0);
        }

        /// <summary>Opens the saved desert scene, bakes its APV, saves it, and exits (batch).</summary>
        public static void BatchBakeSaved()
        {
            var scene = EditorSceneManager.OpenScene(DesertScene, OpenSceneMode.Single);
            Bake(scene, DesertScene, done: ok =>
            {
                EditorSceneManager.SaveScene(scene, DesertScene);
                AssetDatabase.SaveAssets();
                FPSKitBatch.Exit(ok ? 0 : 1);
            });
        }

        /// <summary>
        /// The same, finished before it returns (the scene build runs in one call and exits after it): the bake's own
        /// per-update step is called in a loop instead of on the editor's update.
        /// </summary>
        public static bool BakeNow(UnityEngine.SceneManagement.Scene scene, string path)
        {
            bool result = false;
            Bake(scene, path, ok => result = ok, synchronous: true);
            return result;
        }

        /// <summary>
        /// Prepares the open town scene for APV and bakes it (asynchronously, on the editor's update, unless
        /// <paramref name="synchronous"/>); <paramref name="done"/> is told whether it baked.
        /// </summary>
        public static void Bake(UnityEngine.SceneManagement.Scene scene, string path, Action<bool> done, bool synchronous = false)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            FPSKitQualityTiers.Ensure();
            ApplyRealLightLook();

            // Textures at an eighth of their size for the bake: the GPU here shares the system's memory, so every texture
            // the editor uploads counts against the 14 GB, and with the whole town's at full size the baker was killed by
            // earlyoom with the editor itself showing only 4.4 GB. Bounce colour needs no detail.
            int mipLimit = QualitySettings.globalTextureMipmapLimit;
            QualitySettings.globalTextureMipmapLimit = 3;

            // The pipeline that bakes must be one with APV on: High, for the bake.
            int was = QualitySettings.GetQualityLevel();
            int high = Array.IndexOf(QualitySettings.names, "High");
            if (high >= 0) QualitySettings.SetQualityLevel(high, true);
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null || asset.lightProbeSystem != LightProbeSystem.ProbeVolumes)
            {
                Debug.LogError($"[FPSKit] town GI: the active pipeline asset ({(asset != null ? asset.name : "none")}) has no adaptive probe volumes");
                QualitySettings.globalTextureMipmapLimit = mipLimit;   // (it is a project setting: never left changed)
                QualitySettings.SetQualityLevel(was, true);
                done(false);
                return;
            }

            // One global volume over everything.
            var arena = GameObject.Find("Arena");
            var volume = Object.FindAnyObjectByType<ProbeVolume>();
            if (volume == null)
            {
                var go = new GameObject("TownProbeVolume");
                if (arena != null) go.transform.SetParent(arena.transform, false);
                volume = go.AddComponent<ProbeVolume>();
            }
            // Over the town only (the arena's kilometre and the minaret's height), not the 1.4 km terrain: probes are laid
            // densely along every surface, the open desert round the town included, and over all of it the bake took 10.4 GB
            // and earlyoom killed the editor (this laptop has 14).
            volume.mode = ProbeVolume.Mode.Local;
            volume.transform.position = new Vector3(0f, 30f, 0f);
            volume.transform.rotation = Quaternion.identity;
            volume.transform.localScale = Vector3.one;
            volume.size = new Vector3(TownSpan, 90f, TownSpan);
            volume.fillEmptySpaces = false;

            // The baking set: this scene alone.
            string setPath = path.Replace(".unity", "_APV.asset");
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                set.name = System.IO.Path.GetFileNameWithoutExtension(setPath);
                typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(set, null);
                AssetDatabase.CreateAsset(set, setPath);
            }
            // 3 m wherever there is geometry (a room gets two or three across; the contact shading under and round small
            // things is SSAO's), up to 81 m in open air. At 1 m the town wanted more than Unity's 67,180,350 probes a set.
            set.minDistanceBetweenProbes = 3f;
            set.simplificationLevels = 3;
            // The grid lifted half a spacing: from the ground at 0 its layers fell at 0, 3 and 6 m, in the floor slabs of
            // the town's 3.2 m storeys, and floors read probes buried in them (dark, sky-blue floors under bright walls, the
            // lighting tour). At 1.5 and 4.5 m they are in the middle of each storey's rooms.
            set.probeOffset = new Vector3(0f, 1.5f, 0f);
            set.minRendererVolumeSize = 1f;         // props smaller than a cubic metre do not subdivide the grid (they still shade)
            int backdropLayer = LayerMask.NameToLayer("Backdrop");
            set.renderersLayerMask = backdropLayer >= 0 ? ~(1 << backdropLayer) : ~0;   // the apron and the far scenery lay no probes
            // Probes that land in a wall see its inside and bake black; with dilation off (the default) they kept it and the
            // walls and corners beside them blended it in -- black spots in every room's corners (his first look, 2026-10-24).
            // Dilation fills them from good neighbours within a probe spacing; virtual offset pushes probes near a surface
            // further out of it first.
            var so = new SerializedObject(set);
            void Set(string name, object value)
            {
                var prop = so.FindProperty("settings." + name);
                if (prop == null) { Debug.LogWarning("[FPSKit] town GI: no baking setting " + name); return; }
                switch (value) { case bool b: prop.boolValue = b; break; case int i: prop.intValue = i; break; case float f: prop.floatValue = f; break; }
            }
            Set("dilationSettings.enableDilation", true);
            Set("dilationSettings.dilationDistance", 3.5f);
            Set("dilationSettings.dilationIterations", 2);
            Set("dilationSettings.dilationValidityThreshold", 0.25f);
            Set("virtualOffsetSettings.useVirtualOffset", true);
            Set("virtualOffsetSettings.searchMultiplier", 0.45f);
            Set("virtualOffsetSettings.outOfGeoOffset", 0.05f);
            so.ApplyModifiedPropertiesWithoutUndo();
            set.skyOcclusion = false;
            EditorUtility.SetDirty(set);
            set.TryAddScene(AssetDatabase.AssetPathToGUID(path));   // false when it is in the set already

            // Every static mesh in the arena takes its light from the probes (nothing gets a lightmap). Only the town's own
            // geometry -- buildings, their insides, landmarks, roads, the edge's structures -- shades and bounces light in
            // the bake: the props and pack models (28,000 objects in all, 1,189 meshes, 329 materials) and the window joinery
            // took the CPU baker to 10 GB and earlyoom killed it. They still receive the light where they stand. What moves
            // is left as it is (it takes probe light anyway).
            int statics = 0, bouncing = 0;
            int backdrop = LayerMask.NameToLayer("Backdrop");
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.ContributeGI) != 0) GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.ContributeGI);
            }
            if (arena != null)
                foreach (var r in arena.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.GetComponentInParent<Civilian>() != null || r.GetComponentInParent<EnemyAI>() != null || r.GetComponentInParent<Rigidbody>() != null) continue;
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if (r.gameObject.layer == backdrop) continue;   // far scenery: the sky ambient, as before
                    // An inside with a lightmap (the classic bake before this one, FPSKitTownLightmaps) keeps it.
                    bool lightmapped = r.receiveGI == ReceiveGI.Lightmaps && r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE;
                    if (!lightmapped) r.receiveGI = ReceiveGI.LightProbes;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    statics++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || AssetDatabase.Contains(mf.sharedMesh) || r.name.StartsWith("Detail_")) continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
                    bouncing++;
                }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags | StaticEditorFlags.ContributeGI);
            }

            // How the probes are read (the scene's global post-processing profile): at 3 m apart the defaults' offsets
            // (0.05 along the normal, 0.1 towards the eye) are too short, and a surface read probes buried in the wall or the
            // ceiling behind it -- black ceilings in rooms lit from the door.
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || v.sharedProfile == null) continue;
                var options = v.sharedProfile.TryGet<ProbeVolumesOptions>(out var o) ? o : v.sharedProfile.Add<ProbeVolumesOptions>(true);
                options.normalBias.Override(0.4f);
                options.viewBias.Override(0.3f);
                options.leakReductionMode.Override(APVLeakReductionMode.Quality);
                EditorUtility.SetDirty(v.sharedProfile);
            }

            // The lighting settings: the classic probes' bake keeps its own strength; this bake its own.
            float ambientWas = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = ambientWas * ApvSky;
            var settings = Lightmapping.lightingSettings;
            float classicIndirect = settings != null ? settings.indirectScale : 1f;
            var lightmapper = settings != null ? settings.lightmapper : LightingSettings.Lightmapper.ProgressiveCPU;
            if (settings != null)
            {
                settings.indirectScale = ApvIndirect;
                // The GPU lightmapper needs OpenCL; this laptop's Intel Arc has none unless Intel's runtime is installed
                // (intel-opencl-icd), and without it the bake waited forever on output never written. FPSKIT_GI_GPU=1 asks
                // for the GPU once the runtime is there.
                settings.lightmapper = Environment.GetEnvironmentVariable("FPSKIT_GI_GPU") == "1" && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
                    ? LightingSettings.Lightmapper.ProgressiveGPU : LightingSettings.Lightmapper.ProgressiveCPU;
                settings.indirectSampleCount = 128;
                settings.environmentSampleCount = 128;
                settings.maxBounces = 3;
            }

            // URP sets the probe volumes up only when it makes its pipeline, from the asset active then; one made before APV
            // was switched on (earlier in this session) has none and the bake will not start. So another tier's pipeline is
            // made first, then High's again, by rendering a camera under each.
            var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            void RenderOnce()
            {
                if (cam == null) return;
                var rt = RenderTexture.GetTemporary(64, 64, 24);
                try { RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt }); }
                finally { RenderTexture.ReleaseTemporary(rt); }
            }
            int lowTier = Array.IndexOf(QualitySettings.names, "Low");
            if (lowTier >= 0 && high >= 0)
            {
                QualitySettings.SetQualityLevel(lowTier, true);
                RenderOnce();
                QualitySettings.SetQualityLevel(high, true);
            }
            RenderOnce();
            if (!ProbeReferenceVolume.instance.isInitialized)
                Debug.LogWarning($"[FPSKit] town GI: the probe reference volume is not initialised (pipeline {RenderPipelineManager.currentPipeline?.GetType().Name ?? "none"})");

            // The light transport failing to start (no OpenCL device for the GPU lightmapper) is only logged: the bake then
            // reads output that was never written, every frame, for ever (458,000 exceptions before the editor fell over).
            bool transportFailed = false;
            void Watch(string message, string trace, LogType type)
            {
                if (message.Contains("Light baking could not be started") || message.Contains("PostProcessingOutput/APVBake")) transportFailed = true;
            }
            Application.logMessageReceived += Watch;
            // Everything that does not bounce light is switched off while the bake runs and back on before the scene is
            // saved: the baker snapshots every enabled renderer (28,000 of them, props, cars, people, the window joinery),
            // though only the town's own geometry and the terrain bounce light, and with the snapshot the bake came within a
            // few hundred MB of the 14 GB (earlyoom killed one with the editor at 4.9 GB). The light it bakes is the same.
            var idle = new System.Collections.Generic.List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) == 0) { r.enabled = false; idle.Add(r); }
            var foliage = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => t.drawTreesAndFoliage).ToList();
            foreach (var t in foliage) t.drawTreesAndFoliage = false;

            // Realtime reflection probes stay out of any bake (headless captures drew white: FPSKitTownProbes).
            var reflection = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.enabled).ToList();
            foreach (var p in reflection) p.enabled = false;

            void Finish(bool ok)
            {
                foreach (var r in idle) if (r != null) r.enabled = true;
                foreach (var t in foliage) if (t != null) t.drawTreesAndFoliage = true;
                Application.logMessageReceived -= Watch;
                QualitySettings.globalTextureMipmapLimit = mipLimit;
                RenderSettings.ambientIntensity = ambientWas;
                foreach (var p in reflection) p.enabled = true;
                if (settings != null) { settings.indirectScale = classicIndirect; settings.lightmapper = lightmapper; EditorUtility.SetDirty(settings); }
                QualitySettings.SetQualityLevel(was, true);
                var descs = typeof(ProbeVolumeBakingSet).GetField("cellDescs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(set) as System.Collections.ICollection;
                int cells = descs?.Count ?? 0;
                long bytes = 0;
                // The set's data lies beside it: <set>.Cell*.bytes and <set>-<scenario>.Cell*.bytes.
                string dir = System.IO.Path.GetDirectoryName(setPath), stem = System.IO.Path.GetFileNameWithoutExtension(setPath);
                foreach (var f in System.IO.Directory.GetFiles(dir, stem + "*.bytes")) bytes += new System.IO.FileInfo(f).Length;
                Debug.Log($"[FPSKit] town GI (APV) {(ok ? "baked" : "FAILED")}: {cells} cells, {bytes / 1048576f:0} MB of probe data, {statics} static renderers ({bouncing} bouncing, {idle.Count} switched off for it), " +
                          $"{clock.Elapsed.TotalMinutes:0.0} min, {(settings != null ? settings.lightmapper.ToString() : "?")}");
                // The lightmapped rooms' reflections, captured from the rooms as they are now lit (FPSKitTownLightmaps).
                if (ok) FPSKitTownLightmaps.CaptureReflections(path);
                done(ok);
            }

            if (!AdaptiveProbeVolumes.BakeAsync())
            {
                Debug.LogError("[FPSKit] town GI: the APV bake would not start");
                Finish(false);
                return;
            }
            double lastReport = 0;
            var dataField = typeof(AdaptiveProbeVolumes).GetField("s_BakeData", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            string Status()
            {
                object data = dataField?.GetValue(null);
                if (data == null) return "";
                var t = data.GetType();
                object probes = t.GetField("probeCount")?.GetValue(data), step = t.GetField("step")?.GetValue(data), failed = t.GetField("failed")?.GetValue(data);
                return $", {probes:N0} probes, step {step}{(failed is bool f && f ? ", FAILED" : "")}";
            }
            // Placement is done by the time the bake starts: too many probes, and it stops here, before the lighting.
            long placed = 0;
            {
                object data = dataField?.GetValue(null);
                if (data != null && data.GetType().GetField("probeCount")?.GetValue(data) is int count) placed = count;
                Debug.Log($"[FPSKit] town GI: {placed:N0} probes placed in {clock.Elapsed.TotalMinutes:0.0} min (limit {MaxProbes:N0})");
            }
            if (placed > MaxProbes)
            {
                Debug.LogError($"[FPSKit] town GI: {placed:N0} probes is over the limit; not baked");
                AdaptiveProbeVolumes.Cancel();
                Finish(false);
                return;
            }
            bool Failed()
            {
                if (transportFailed) return true;
                object data = dataField?.GetValue(null);
                return data != null && data.GetType().GetField("failed")?.GetValue(data) is bool f && f;
            }
            if (synchronous)
            {
                var step = typeof(AdaptiveProbeVolumes).GetMethod("AsyncBakeCallback", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (step != null)
                {
                    EditorApplication.update -= (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), step);
                    while (AdaptiveProbeVolumes.isRunning)
                    {
                        step.Invoke(null, null);
                        if (clock.Elapsed.TotalSeconds - lastReport > 60) { lastReport = clock.Elapsed.TotalSeconds; Debug.Log($"[FPSKit] town GI: baking, {clock.Elapsed.TotalMinutes:0.0} min{Status()}"); }
                        if (Failed()) { AdaptiveProbeVolumes.Cancel(); break; }
                    }
                    Finish(set.HasBakedData() && !transportFailed);
                    return;
                }
            }
            void Pump()
            {
                if (AdaptiveProbeVolumes.isRunning && !Failed())
                {
                    if (clock.Elapsed.TotalSeconds - lastReport > 60) { lastReport = clock.Elapsed.TotalSeconds; Debug.Log($"[FPSKit] town GI: baking, {clock.Elapsed.TotalMinutes:0.0} min{Status()}"); }
                    return;
                }
                if (AdaptiveProbeVolumes.isRunning) AdaptiveProbeVolumes.Cancel();
                EditorApplication.update -= Pump;
                Finish(set.HasBakedData() && !transportFailed);
            }
            EditorApplication.update += Pump;
        }
    }
}
#endif
