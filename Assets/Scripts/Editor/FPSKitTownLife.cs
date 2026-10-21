#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What moves in the desert town, and what is parked: civilians (prefabs from the user's Adventure man with the
    /// Starter Assets and Mixamo clips, a <see cref="CivilianDirector"/> with the places people go and the doors they
    /// run to), parked cars at the kerbs, in yards, at the pumps and the garage, wrecks in the junkyard, the signal
    /// controllers (<see cref="TownSignals"/>), and the traffic: routes through the street network for a
    /// <see cref="TrafficDirector"/> -- right-hand lanes, turns curved through each junction, stop lines with the rule
    /// that applies there (signal, stop sign, give way), the roundabout taken anticlockwise.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static readonly List<TrafficDirector.Route> _routes = new List<TrafficDirector.Route>();

        private static void ResetTownLife()
        {
            _routes.Clear(); _civicSpots.Clear(); _signalHeads.Clear(); _gasPumps.Clear(); _junkRows.Clear();
            _parkedPrefabs.Clear(); _parkedMade.Clear(); _wreckHole = null;
            _palaceCar = Vector4.zero; _garageBay = Vector4.zero; _busStop = Vector4.zero; _campSpot = Vector4.zero;
            _soukStreet = default; _oasisWater = 0f;
        }

        private static readonly string[] SedanNames = { "Car_Sedan_A", "Car_Sedan_B", "Car_Sedan_C", "Car_Sedan_D" };

        private static void BuildTownLife(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Life").transform;
            group.SetParent(root, false);
            var parked = new GameObject("ParkedCars").transform;
            parked.SetParent(group, false);

            PlaceParkedCars(parked, rng);
            PlaceDrivableCars(group);
            BuildTechnicals(group);
            var signals = BuildSignalControllers(group);
            BuildRoutes(rng, signals);

            var life = new GameObject("TownLife");
            life.transform.SetParent(group, false);

            var civ = life.AddComponent<CivilianDirector>();
            civ.maxCivilians = _theme.townCivilians;
            civ.prefabs = CivilianPrefabs();
            civ.spots = CivilianSpots().ToArray();
            civ.doors = _townDoors.ToArray();

            var traffic = life.AddComponent<TrafficDirector>();
            traffic.maxCars = _theme.townTrafficCars;
            traffic.routes = _routes.ToArray();
            traffic.signals = signals;
            traffic.carPrefabs = SedanNames.Select(n => TrafficPrefab(n)).Where(p => p != null).ToArray();
            traffic.obstacleMask = LayerMask.GetMask("Vehicle", "Player", "Civilian", "Enemy");

            Debug.Log($"[FPSKit] town life: {civ.prefabs.Length} civilian kinds, {civ.spots.Length} spots, {civ.doors.Length} doors; " +
                      $"{_routes.Count} traffic routes ({_routes.Sum(r => r.points.Length)} points), {signals.Length} signalled junctions, " +
                      $"{parked.childCount} parked cars.");
        }

        // ==================================================================
        // Cars
        // ==================================================================
        /// <summary>
        /// A car from the pack placed with its length along its own z: measured from its bounds, turned a quarter if the
        /// pack modelled it along x, its base on the ground and centred. Returned as a parent holding the body.
        /// </summary>
        /// <summary>
        /// A parked car's prefab: the pack car with each level of detail merged into one mesh (a submesh per material),
        /// shared by every parked copy, and only two levels kept. The pack's car is 224 objects (some fifty parts, each at
        /// five levels); 282 of them made 63 000 objects, the largest single share of the town's memory. Cached per kind
        /// under FPSKit_Generated/Town/Parked.
        /// </summary>
        private static GameObject ParkedCarPrefab(string packName)
        {
            string dir = FPSKitTownAssets.Out + "/Parked";
            string path = $"{dir}/Parked_{packName}.prefab";
            if (_parkedPrefabs.TryGetValue(packName, out var cached) && cached != null) return cached;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && _parkedMade.Contains(packName)) { _parkedPrefabs[packName] = existing; return existing; }
            var src = PackPrefab(packName);
            if (src == null) return null;
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(FPSKitTownAssets.Out, "Parked");

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
            var group = inst.GetComponentInChildren<LODGroup>();
            var levels = new List<Renderer[]>();
            float[] heights;
            if (group != null)
            {
                var lods = group.GetLODs();
                // The first level and the middle one (LOD2 when there is one), and where each hands over.
                int mid = Mathf.Min(2, lods.Length - 1);
                levels.Add(lods[0].renderers);
                if (mid > 0) levels.Add(lods[mid].renderers);
                heights = mid > 0 ? new[] { lods[mid - 1].screenRelativeTransitionHeight, lods[lods.Length - 1].screenRelativeTransitionHeight }
                                  : new[] { lods[lods.Length - 1].screenRelativeTransitionHeight };
            }
            else { levels.Add(inst.GetComponentsInChildren<Renderer>()); heights = new[] { 0.01f }; }

            var root = new GameObject("Parked_" + packName);
            var lodList = new List<LOD>();
            for (int li = 0; li < levels.Count; li++)
            {
                var byMat = new Dictionary<Material, List<CombineInstance>>();
                foreach (var r in levels[li])
                {
                    if (r == null || !(r is MeshRenderer)) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mats = r.sharedMaterials;
                    for (int sm = 0; sm < mf.sharedMesh.subMeshCount && sm < mats.Length; sm++)
                    {
                        if (mats[sm] == null) continue;
                        if (!byMat.TryGetValue(mats[sm], out var list)) byMat[mats[sm]] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sm, transform = r.transform.localToWorldMatrix });
                    }
                }
                var parts = new List<Mesh>(); var partMats = new List<Material>();
                foreach (var kv in byMat)
                {
                    var part = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    part.CombineMeshes(kv.Value.ToArray(), true, true);
                    parts.Add(part); partMats.Add(kv.Key);
                }
                var merged = new Mesh { name = $"Parked_{packName}_LOD{li}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                merged.CombineMeshes(parts.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
                foreach (var m in parts) Object.DestroyImmediate(m);
                UnityEditor.MeshUtility.SetMeshCompression(merged, ModelImporterMeshCompression.Low);
                string meshPath = $"{dir}/Parked_{packName}_LOD{li}.asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (old != null) { EditorUtility.CopySerialized(merged, old); Object.DestroyImmediate(merged); merged = old; }
                else AssetDatabase.CreateAsset(merged, meshPath);
                var child = new GameObject("LOD" + li);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = merged;
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = partMats.ToArray();
                lodList.Add(new LOD(heights[Mathf.Min(li, heights.Length - 1)], new Renderer[] { mr }));
            }
            Object.DestroyImmediate(inst);
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(lodList.ToArray());
            lg.RecalculateBounds();
            DynamicLighting(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            _parkedPrefabs[packName] = prefab; _parkedMade.Add(packName);
            return prefab;
        }
        private static readonly Dictionary<string, GameObject> _parkedPrefabs = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> _parkedMade = new HashSet<string>();

        private static GameObject PlaceCar(Transform parent, string packName, Vector3 pos, float yaw, Material overrideAll = null, bool wreck = false)
        {
            var prefab = packName.StartsWith("PH:") ? Prop(packName.Substring(3)) : (ParkedCarPrefab(packName) ?? PackPrefab(packName));
            if (prefab == null) return null;
            var holder = new GameObject(packName.Replace("PH:", ""));
            holder.transform.SetParent(parent, false);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            var b = WorldBounds(body);
            if (b.size.x > b.size.z * 1.15f) { body.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); b = WorldBounds(body); }
            body.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = WorldBounds(body);
            holder.transform.position = pos;
            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (overrideAll != null || wreck)
                foreach (var r in body.GetComponentsInChildren<Renderer>())
                {
                    // Per material (a parked car is one renderer with every material): a wreck's glass is gone, so the
                    // window openings read as the dark inside of the shell.
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        bool glass = mats[i] != null && (mats[i].name.ToLowerInvariant().Contains("glass") || mats[i].name.ToLowerInvariant().Contains("window"));
                        mats[i] = wreck && glass ? WreckHole() : overrideAll ?? mats[i];
                    }
                    r.sharedMaterials = mats;
                }
            if (holder.GetComponentInChildren<Collider>() == null)
            {
                var box = holder.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, b.size.y * 0.5f, 0f);
                box.size = new Vector3(b.size.x * 0.95f, b.size.y * 0.9f, b.size.z * 0.97f);
            }
            SetLayerRecursive(holder, LayerMask.NameToLayer("Environment"));
            SetTagRecursive(holder, "Metal");
            DynamicLighting(holder);
            NoStanding(holder);
            GameObjectUtility.SetStaticEditorFlags(holder, StaticEditorFlags.BatchingStatic);
            foreach (Transform t in holder.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            var fp = new Vector2(pos.x, pos.z);
            _townSolids.Add(new Rect(fp - Vector2.one * 2.3f, Vector2.one * 4.6f));
            return holder;
        }

        private static Material _wreckHole;
        private static Material WreckHole() => _wreckHole != null ? _wreckHole : (_wreckHole = TownFlat("WreckHole", new Color(0.03f, 0.028f, 0.026f), 0.05f));
        private static Material _burnt;
        private static Material Burnt() => _burnt != null ? _burnt : (_burnt = TownFlat("BurntWreck", new Color(0.09f, 0.075f, 0.065f), 0.12f, 0.3f));
        private static Material RustBody() => TownMat("RustBody", "rusty_corrugated_iron", new Color(0.85f, 0.7f, 0.6f), 1.5f, 0.5f);

        private static void PlaceParkedCars(Transform parked, System.Random rng)
        {
            int budget = _theme.townParkedCars, placed = 0;
            string Pick() => rng.NextDouble() < 0.06 ? "PH:covered_car" : SedanNames[rng.Next(SedanNames.Length)];

            // Kerbside, both sides of every street, boulevard and avenue, clear of junctions, the roundabout, the
            // checkpoint and the gas station's entrance.
            var roads = _townRoads.Where(r => r.Paved && r.kind != RoadKind.Highway).OrderBy(_ => rng.Next()).ToList();
            foreach (var r in roads)
            {
                if (placed >= budget) break;
                var clear = new List<Vector2>();
                foreach (var j in JunctionsOn(r)) { var o = j.x == r ? j.z : j.x; float at = r.AlongX ? j.at.x : j.at.y; clear.Add(new Vector2(at - o.Corridor - 9f, at + o.Corridor + 9f)); }
                float c = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                if (Mathf.Abs(r.Fixed - (r.AlongX ? RoundaboutCentre.y : RoundaboutCentre.x)) < 1f) clear.Add(new Vector2(c - RoundaboutWalk - 12f, c + RoundaboutWalk + 12f));
                if (r.kind == RoadKind.Avenue)
                {
                    clear.Add(new Vector2(-400f, -340f));
                    clear.Add(new Vector2(BusStopAt.y - 10f, BusStopAt.y + 10f));
                    clear.Add(new Vector2(TownStart.z - 24f, TownStart.z + 6f));      // the drivable car by the start
                }
                float lane = r.Half - 1.0f;
                foreach (float side in new[] { -1f, 1f })
                    foreach (var (a, b) in Minus(r.Min + 6f, r.Max - 6f, new List<Vector2>(clear)))
                        for (float s = a + Rand(rng, 1f, 6f); s < b - 3f; s += Rand(rng, 5.6f, 8.5f))
                        {
                            if (placed >= budget) break;
                            if (rng.NextDouble() < 0.55) continue;
                            var p = RoadPoint(r, s, side * lane, 0.02f);
                            // Facing the way traffic on that side drives (the right-hand lane): +across drives +along.
                            float yaw = r.AlongX ? (side > 0f ? 90f : -90f) : (side > 0f ? 0f : 180f);
                            if (PlaceCar(parked, Pick(), p, yaw + Rand(rng, -2.5f, 2.5f)) != null) placed++;
                        }
            }
            // Yards, the palace, the pumps, the garage.
            foreach (var y in _yardCars)
            {
                var p = new Vector3(y.x, GroundHeightAt(y.x, y.z), y.z);
                PlaceCar(parked, SedanNames[rng.Next(SedanNames.Length)], p, y.w);
            }
            if (_palaceCar != Vector4.zero) PlaceCar(parked, "Car_Sport", new Vector3(_palaceCar.x, GroundHeightAt(_palaceCar.x, _palaceCar.z), _palaceCar.z), _palaceCar.w);
            foreach (var g in _gasPumps)
                if (rng.NextDouble() < 0.6) PlaceCar(parked, SedanNames[rng.Next(SedanNames.Length)], new Vector3(g.x, g.y, g.z), 0f);
            if (_garageBay != Vector4.zero)
            {
                PlaceCar(parked, "Car_Touring", new Vector3(_garageBay.x - 5f, _garageBay.y, _garageBay.z), _garageBay.w);
                PlaceCar(parked, SedanNames[0], new Vector3(_garageBay.x + 5f, _garageBay.y, _garageBay.z), _garageBay.w + 8f, RustBody());
            }
            if (_campSpot != Vector4.zero) PlaceCar(parked, SedanNames[2], new Vector3(_campSpot.x, GroundHeightAt(_campSpot.x, _campSpot.z), _campSpot.z), _campSpot.w);
            // Vacant lots: a wreck in some, a parked car or two in others.
            foreach (var v in _vacantLots)
            {
                if (rng.NextDouble() < 0.5) continue;
                var p = new Vector2(Rand(rng, v.xMin + 3f, v.xMax - 3f), Rand(rng, v.yMin + 3f, v.yMax - 3f));
                bool wreck = rng.NextDouble() < 0.5;
                PlaceCar(parked, SedanNames[rng.Next(SedanNames.Length)], new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f), wreck ? Burnt() : null, wreck);
            }
            // The junkyard: rows of wrecks, some stacked two high.
            foreach (var row in _junkRows)
                for (float x = row.x; x < row.w; x += Rand(rng, 4.8f, 6f))
                {
                    // A drive runs south from the gate between the rows, and a second gap further east: nose to tail, each
                    // row was a wall across the yard and the aisles between them could not be reached.
                    if (Mathf.Abs(x - JunkGateX) < 4.5f || Mathf.Abs(x - (row.x + row.w) * 0.5f - 30f) < 3f) continue;
                    var mat = rng.NextDouble() < 0.5 ? Burnt() : RustBody();
                    var p = new Vector3(x, GroundHeightAt(x, row.z), row.z + Rand(rng, -0.6f, 0.6f));
                    var car = PlaceCar(parked, SedanNames[rng.Next(SedanNames.Length)], p, 90f + Rand(rng, -12f, 12f), mat, wreck: true);
                    if (car != null && rng.NextDouble() < 0.35)
                    {
                        var top = PlaceCar(parked, SedanNames[rng.Next(SedanNames.Length)], p + Vector3.up * 1.35f, 90f + Rand(rng, -25f, 25f), rng.NextDouble() < 0.5 ? Burnt() : RustBody(), wreck: true);
                        if (top != null) top.transform.rotation *= Quaternion.Euler(Rand(rng, -6f, 6f), 0f, Rand(rng, -8f, 8f));
                    }
                }
        }

        /// <summary>
        /// The moving version of a pack car: its body under a root at the ground, a box collider, a kinematic body, a
        /// non-carving obstacle (agents step round it; carving every frame would re-cut the navmesh constantly), and
        /// the <see cref="TrafficCar"/>. Saved once per kind under FPSKit_Generated/Town.
        /// </summary>
        private static GameObject TrafficPrefab(string packName)
        {
            string path = $"{FPSKitTownAssets.Out}/Traffic_{packName}.prefab";
            var src = PackPrefab(packName);
            if (src == null) return null;
            var root = new GameObject(packName);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            var b = WorldBounds(body);
            if (b.size.x > b.size.z * 1.15f) { body.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); b = WorldBounds(body); }
            body.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = WorldBounds(body);
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            box.size = new Vector3(b.size.x * 0.95f, b.size.y * 0.9f, b.size.z * 0.97f);
            box.sharedMaterial = FPSKitImpactAssets.Surface("Metal");      // sparks, not dust (FPSImpactManager)
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var obs = root.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.center = box.center; obs.size = box.size;
            obs.carving = false;
            var car = root.AddComponent<TrafficCar>();
            car.halfLength = b.size.z * 0.5f;
            car.halfWidth = b.size.x * 0.5f;
            SetLayerRecursive(root, LayerMask.NameToLayer("Vehicle") >= 0 ? LayerMask.NameToLayer("Vehicle") : 0);
            SetTagRecursive(root, "Metal");
            DynamicLighting(root);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // ==================================================================
        // Cars you can drive
        // ==================================================================
        /// <summary>
        /// Three cars the player can get into (DrivableCar): one in the parking lane by the start, one at the pumps, one
        /// outside the garage. Each is the pack car on a Rigidbody with four WheelColliders put where its wheels are
        /// (found by name; the corners of its body if the pack names them otherwise).
        /// </summary>
        private static void PlaceDrivableCars(Transform group)
        {
            var root = new GameObject("DrivableCars").transform;
            root.SetParent(group, false);
            var spots = new List<Vector4>
            {
                new Vector4(TownStart.x - 3.2f, 0f, TownStart.z - 14f, 0f),
                new Vector4(GasStationRect.xMax - 10f, 0f, GasStationRect.yMin + 4f, 90f),
                _garageBay != Vector4.zero ? new Vector4(_garageBay.x, 0f, _garageBay.z - 4f, 0f) : Vector4.zero,
            };
            string[] kinds = { "Car_Sedan_B", "Car_Sedan_C", "Car_Sedan_D" };
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i] == Vector4.zero) continue;
                var p = new Vector3(spots[i].x, 0f, spots[i].z);
                p.y = RoadAt(new Vector2(p.x, p.z)) != null ? TownRoadY(p.x, p.z) + 0.02f : GroundHeightAt(p.x, p.z);
                MakeDrivable(root, kinds[i % kinds.Length], p, spots[i].w);
            }
        }

        private static GameObject MakeDrivable(Transform parent, string packName, Vector3 pos, float yaw)
        {
            var src = PackPrefab(packName);
            if (src == null) return null;
            var holder = new GameObject("Drivable_" + packName);
            holder.transform.SetParent(parent, false);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(src, holder.transform);
            var b = WorldBounds(body);
            if (b.size.x > b.size.z * 1.15f) { body.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); b = WorldBounds(body); }
            body.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = WorldBounds(body);
            foreach (var c in holder.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var box = holder.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, b.size.y * 0.55f, 0f);
            box.size = new Vector3(b.size.x * 0.95f, b.size.y * 0.7f, b.size.z * 0.97f);
            var rb = holder.AddComponent<Rigidbody>();
            var car = holder.AddComponent<DrivableCar>();

            // Wheels: the pack's named wheel roots, front-left, front-right, rear-left, rear-right.
            string[] names = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
            var visuals = new Transform[4];
            foreach (var t in body.GetComponentsInChildren<Transform>())
                for (int k = 0; k < 4; k++)
                    if (visuals[k] == null && t.name.Contains("Wheel" + names[k])) visuals[k] = t;
            float radius = 0.34f;
            var cols = new WheelCollider[4];
            for (int k = 0; k < 4; k++)
            {
                Vector3 at;
                if (visuals[k] != null)
                {
                    at = holder.transform.InverseTransformPoint(visuals[k].position);
                    var r = visuals[k].GetComponentInChildren<Renderer>();
                    if (r != null) radius = Mathf.Max(0.25f, r.bounds.extents.y);
                }
                else at = new Vector3((k % 2 == 0 ? -1f : 1f) * b.size.x * 0.4f, radius, (k < 2 ? 1f : -1f) * b.size.z * 0.33f);
                var wgo = new GameObject("Wheel" + names[k]);
                wgo.transform.SetParent(holder.transform, false);
                wgo.transform.localPosition = at + Vector3.up * 0.12f;
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = radius;
                wc.mass = 22f;
                wc.suspensionDistance = 0.22f;
                var spring = wc.suspensionSpring; spring.spring = 32000f; spring.damper = 4200f; spring.targetPosition = 0.45f; wc.suspensionSpring = spring;
                var fwd = wc.forwardFriction; fwd.stiffness = 1.4f; wc.forwardFriction = fwd;
                var side = wc.sidewaysFriction; side.stiffness = 1.6f; wc.sidewaysFriction = side;
                cols[k] = wc;
            }
            car.frontLeft = cols[0]; car.frontRight = cols[1]; car.rearLeft = cols[2]; car.rearRight = cols[3];
            car.wheelVisuals = visuals;
            car.exitOffset = new Vector3(-(b.size.x * 0.5f + 0.9f), 0f, 0.3f);

            holder.transform.position = pos;
            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            SetLayerRecursive(holder, LayerMask.NameToLayer("Vehicle") >= 0 ? LayerMask.NameToLayer("Vehicle") : 0);
            SetTagRecursive(holder, "Metal");
            DynamicLighting(holder);
            var obs = holder.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box; obs.center = box.center; obs.size = box.size; obs.carving = false;
            var mod = holder.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            mod.ignoreFromBuild = true;
            _ = rb;
            return holder;
        }

        // ==================================================================
        // Technicals
        // ==================================================================
        /// <summary>
        /// Two enemy technicals (TechnicalTruck), each parked off the map on the highway until a level calls it in: one
        /// comes from the east up East 130 to the residential streets, one from the west up West 240 to the water tower.
        /// </summary>
        private static void BuildTechnicals(Transform group)
        {
            var root = new GameObject("Technicals").transform;
            root.SetParent(group, false);
            var hw = _townRoads.Find(r => r.kind == RoadKind.Highway);
            var east130 = _townRoads.Find(r => r.name == "East130N");
            var west240 = _townRoads.Find(r => r.name == "West240N");
            if (hw == null) return;
            if (east130 != null) MakeTechnical(root, TechnicalRoute(hw, -1, east130, 1, 680f, -185f), "Technical_East");
            if (west240 != null) MakeTechnical(root, TechnicalRoute(hw, 1, west240, 1, -680f, -300f), "Technical_West");
        }

        /// <summary>Along the highway lane from <paramref name="fromX"/> in direction <paramref name="d"/>, a right turn up the side street, to <paramref name="stopZ"/>.</summary>
        private static Vector3[] TechnicalRoute(TownRoad hw, int d, TownRoad side, int d2, float fromX, float stopZ)
        {
            var pts = new List<Vector3>();
            float turnAt = side.Fixed - d * (side.Corridor + 4f);
            int n = Mathf.CeilToInt(Mathf.Abs(turnAt - fromX) / 15f);
            for (int k = 0; k <= n; k++) pts.Add(LanePoint(hw, Mathf.Lerp(fromX, turnAt, k / (float)n), d));
            var a = pts[pts.Count - 1];
            var b = LanePoint(side, hw.Fixed + d2 * (hw.Half + 4f), d2);
            var ctrl = new Vector3(LanePoint(side, hw.Fixed, d2).x, (a.y + b.y) * 0.5f, a.z);
            for (int k = 1; k <= 8; k++) { float t = k / 8f; pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b); }
            float start = hw.Fixed + d2 * (hw.Half + 4f);
            n = Mathf.CeilToInt(Mathf.Abs(stopZ - start) / 12f);
            for (int k = 1; k <= n; k++) pts.Add(LanePoint(side, Mathf.Lerp(start, stopZ, k / (float)n), d2));
            return pts.ToArray();
        }

        private static void MakeTechnical(Transform parent, Vector3[] route, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var paint = TownFlat("TechnicalPaint", new Color(0.78f, 0.72f, 0.60f), 0.35f, 0.2f);
            var trim = TownFlat("TechnicalTrim", new Color(0.10f, 0.10f, 0.10f), 0.3f);
            var tyre = TownFlat("Tyre", new Color(0.04f, 0.04f, 0.04f), 0.15f);
            var rim = TownFlat("Rim", new Color(0.55f, 0.55f, 0.55f), 0.5f, 0.8f);
            var lamp = TownFlat("Headlamp", new Color(0.9f, 0.9f, 0.85f), 0.9f);
            var m = new TownMesh();
            // A pickup about 5.3 m long: chassis, cab with its windows, bonnet, the bed with its sides, bumpers, lights.
            m.BoxMinMax(trim, new Vector3(-0.85f, 0.38f, -2.6f), new Vector3(0.85f, 0.62f, 2.6f));
            m.BoxMinMax(paint, new Vector3(-0.9f, 0.62f, 0.85f), new Vector3(0.9f, 1.15f, 2.6f));                 // bonnet
            m.BoxMinMax(paint, new Vector3(-0.9f, 0.62f, -0.4f), new Vector3(0.9f, 1.2f, 0.85f));                 // cab lower
            m.BoxMinMax(paint, new Vector3(-0.84f, 1.2f, -0.35f), new Vector3(0.84f, 1.85f, 0.6f), bottom: false); // cab upper
            m.Quad(_glassDark, new Vector3(-0.8f, 1.25f, 0.61f), new Vector3(0.8f, 1.25f, 0.61f), new Vector3(0.78f, 1.8f, 0.45f), new Vector3(-0.78f, 1.8f, 0.45f), new Vector3(0, 0.3f, 1f));
            foreach (float sx in new[] { -0.85f, 0.85f })
                m.Quad(_glassDark, new Vector3(sx, 1.25f, -0.25f), new Vector3(sx, 1.25f, 0.55f), new Vector3(sx, 1.78f, 0.5f), new Vector3(sx, 1.78f, -0.25f), new Vector3(Mathf.Sign(sx), 0, 0));
            m.Quad(_glassDark, new Vector3(-0.75f, 1.3f, -0.36f), new Vector3(0.75f, 1.3f, -0.36f), new Vector3(0.75f, 1.75f, -0.36f), new Vector3(-0.75f, 1.75f, -0.36f), Vector3.back);
            m.BoxMinMax(paint, new Vector3(-0.9f, 0.62f, -2.6f), new Vector3(0.9f, 0.72f, -0.45f));               // bed floor
            m.BoxMinMax(paint, new Vector3(-0.9f, 0.72f, -2.6f), new Vector3(-0.82f, 1.2f, -0.45f));
            m.BoxMinMax(paint, new Vector3(0.82f, 0.72f, -2.6f), new Vector3(0.9f, 1.2f, -0.45f));
            m.BoxMinMax(paint, new Vector3(-0.9f, 0.72f, -2.6f), new Vector3(0.9f, 1.2f, -2.52f));
            m.BoxMinMax(trim, new Vector3(-0.95f, 0.4f, 2.55f), new Vector3(0.95f, 0.65f, 2.75f));
            m.BoxMinMax(trim, new Vector3(-0.95f, 0.4f, -2.75f), new Vector3(0.95f, 0.6f, -2.58f));
            foreach (float sx in new[] { -0.62f, 0.62f }) m.BoxMinMax(lamp, new Vector3(sx - 0.2f, 0.9f, 2.6f), new Vector3(sx + 0.2f, 1.05f, 2.62f));
            // Pedestal for the gun.
            m.Cylinder(trim, new Vector3(0f, 0.72f, -1.1f), 0.09f, 1.05f, 10);
            var body = m.Build(root.transform, "Body", LayerMask.NameToLayer("Environment"), "Metal", collider: false);
            _ = body;
            // Wheels: cylinders on their sides.
            foreach (var (x, z) in new[] { (-0.85f, 1.6f), (0.85f, 1.6f), (-0.85f, -1.65f), (0.85f, -1.65f) })
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(w.GetComponent<Collider>());
                w.name = "Wheel";
                w.transform.SetParent(root.transform, false);
                w.transform.localPosition = new Vector3(x, 0.38f, z);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                w.transform.localScale = new Vector3(0.76f, 0.14f, 0.76f);
                w.GetComponent<MeshRenderer>().sharedMaterial = tyre;
                var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(hub.GetComponent<Collider>());
                hub.transform.SetParent(w.transform, false);
                hub.transform.localScale = new Vector3(0.6f, 1.05f, 0.6f);
                hub.GetComponent<MeshRenderer>().sharedMaterial = rim;
            }
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.95f, 0f); box.size = new Vector3(1.85f, 1.3f, 5.4f);
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true;

            // The gun on a yaw pivot, and its gunner standing behind it.
            var yaw = new GameObject("GunYaw").transform;
            yaw.SetParent(root.transform, false);
            yaw.localPosition = new Vector3(0f, 1.78f, -1.1f);
            var gun = PackPrefab("Gun_M2");
            Transform muzzle = null;
            if (gun != null)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(gun, yaw);
                g.transform.localPosition = new Vector3(0f, 0f, 0.35f);
                g.transform.localRotation = Quaternion.identity;
                var gb = WorldBounds(g);
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(yaw, false);
                muzzle.position = new Vector3(gb.center.x, gb.center.y, gb.max.z);
            }
            Health gunnerHealth = null; Animator gunnerAnim = null;
            var man = PackPrefab("Civilian_Man_02");
            if (man != null)
            {
                var gm = (GameObject)PrefabUtility.InstantiatePrefab(man, root.transform);
                PrefabUtility.UnpackPrefabInstance(gm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                gm.name = "Gunner";
                gm.transform.localPosition = new Vector3(0f, 0.72f, -1.75f);
                gm.transform.localRotation = Quaternion.identity;
                gunnerAnim = gm.GetComponentInChildren<Animator>();
                if (gunnerAnim != null)
                {
                    gunnerAnim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{FPSKitTownAssets.Out}/Civilian.controller");
                    gunnerAnim.applyRootMotion = false;
                }
                var cap = gm.AddComponent<CapsuleCollider>(); cap.radius = 0.3f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
                gunnerHealth = gm.AddComponent<Health>();
                gunnerHealth.maxHealth = 120f; gunnerHealth.destroyOnDeath = false;
                SetLayerRecursive(gm, LayerMask.NameToLayer("Enemy"));
                SetTagRecursive(gm, "Flesh");
            }
            var tracerGo = new GameObject("Tracer");
            tracerGo.transform.SetParent(root.transform, false);
            var lr = tracerGo.AddComponent<LineRenderer>();
            lr.positionCount = 2; lr.startWidth = 0.05f; lr.endWidth = 0.02f; lr.useWorldSpace = true; lr.enabled = false;
            lr.sharedMaterial = EmissiveFlat("TracerYellow", new Color(1f, 0.8f, 0.3f), 6f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var audio = root.AddComponent<AudioSource>();
            audio.spatialBlend = 1f; audio.minDistance = 8f; audio.maxDistance = 260f; audio.pitch = 0.72f; audio.playOnAwake = false;

            var t = root.AddComponent<TechnicalTruck>();
            t.route = route;
            t.gunYaw = yaw; t.muzzle = muzzle; t.gunner = gunnerHealth; t.gunnerAnimator = gunnerAnim;
            t.tracer = lr; t.audioSource = audio;
            t.shot = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/weapon_fire.wav");
            // Home: the first route point, off the map on the highway, facing along the route.
            root.transform.position = route[0];
            root.transform.rotation = Quaternion.LookRotation((route[1] - route[0]).normalized);
            foreach (var c in root.GetComponentsInChildren<Collider>()) if (c.gameObject.name != "Gunner") c.gameObject.tag = "Metal";
            DynamicLighting(root.gameObject);
            root.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
        }

        // ==================================================================
        // Signals
        // ==================================================================
        private static TownSignals[] BuildSignalControllers(Transform group)
        {
            var list = new List<TownSignals>();
            int k = 0;
            foreach (var (j, heads) in _signalHeads)
            {
                var go = new GameObject("JunctionSignals");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(j.at.x, TownRoadY(j.at.x, j.at.y), j.at.y);
                var s = go.AddComponent<TownSignals>();
                s.offset = k++ * 7.3f;
                s.red = _signalRed; s.amberLit = _signalAmber; s.greenLit = _signalGreen; s.off = _signalOff;
                Renderer[] Lamps(bool alongX, string n) => heads.Where(h => h.alongX == alongX).Select(h => h.head.transform.Find(n)?.GetComponent<Renderer>()).Where(r => r != null).ToArray();
                s.xRed = Lamps(true, "Red"); s.xAmber = Lamps(true, "Amber"); s.xGreen = Lamps(true, "Green");
                s.zRed = Lamps(false, "Red"); s.zAmber = Lamps(false, "Amber"); s.zGreen = Lamps(false, "Green");
                list.Add(s);
            }
            return list.ToArray();
        }

        // ==================================================================
        // Routes
        // ==================================================================
        private static float LaneOffset(TownRoad r) => r.kind == RoadKind.Highway ? 1.75f : r.kind == RoadKind.Avenue ? r.median * 0.5f + 1.75f : 1.6f;

        private static float LaneSpeed(TownRoad r) => r.kind == RoadKind.Highway ? 20f : r.kind == RoadKind.Avenue ? 12f : r.kind == RoadKind.Boulevard ? 11f : 8.5f;

        private const float RingLane = 20f;

        private static bool TouchesRing(TownRoad r, out int endDir)
        {
            endDir = 0;
            if (Vector2.Distance(r.b, RoundaboutCentre) < RoundaboutOuter + 3f) endDir = r.AlongX ? (int)Mathf.Sign(r.b.x - r.a.x) : (int)Mathf.Sign(r.b.y - r.a.y);
            else if (Vector2.Distance(r.a, RoundaboutCentre) < RoundaboutOuter + 3f) endDir = r.AlongX ? (int)Mathf.Sign(r.a.x - r.b.x) : (int)Mathf.Sign(r.a.y - r.b.y);
            return endDir != 0;
        }

        /// <summary>The world point on road <paramref name="r"/>'s lane for driving direction <paramref name="d"/> at <paramref name="along"/>.</summary>
        private static Vector3 LanePoint(TownRoad r, float along, int d) => RoadPoint(r, along, d * LaneOffset(r), 0.02f);

        /// <summary>Whether a road runs on from a junction in direction <paramref name="d"/> (along its own axis).</summary>
        private static bool Continues(TownRoad r, float at, int d) => d > 0 ? r.Max > at + 8f : r.Min < at - 8f;

        /// <summary>Whether driving road <paramref name="r"/> in direction <paramref name="d"/> from <paramref name="from"/> reaches somewhere (a junction, the ring, or the map edge).</summary>
        private static bool Leads(TownRoad r, float from, int d)
        {
            foreach (var j in JunctionsOn(r)) { float at = r.AlongX ? j.at.x : j.at.y; if (d > 0 ? at > from + 1f : at < from - 1f) return true; }
            if (TouchesRing(r, out int e) && e == d) return true;
            return r.kind == RoadKind.Highway;
        }

        private static void BuildRoutes(System.Random rng, TownSignals[] signals)
        {
            _routes.Clear();
            var sigIndex = new Dictionary<TownJunction, int>();
            for (int i = 0; i < _signalHeads.Count; i++) sigIndex[_signalHeads[i].j] = i;
            var drivable = _townRoads.Where(r => r.Paved).ToList();
            float total = drivable.Sum(r => r.Length);

            for (int n = 0; n < 70; n++)
            {
                // Start somewhere on a road weighted by length, either way along it.
                float pick = Rand(rng, 0f, total);
                TownRoad r = drivable[0];
                foreach (var c in drivable) { if (pick < c.Length) { r = c; break; } pick -= c.Length; }
                int d = rng.NextDouble() < 0.5 ? 1 : -1;
                float s = Rand(rng, r.Min + 10f, r.Max - 10f);
                if (!Leads(r, s, d)) d = -d;
                if (!Leads(r, s, d)) continue;

                var pts = new List<Vector3>(); var speeds = new List<float>();
                var stopI = new List<int>(); var stopR = new List<int>(); var stopJ = new List<int>(); var stopX = new List<byte>();
                void Add(Vector3 p, float v) { if (pts.Count > 0 && (pts[pts.Count - 1] - p).sqrMagnitude < 0.25f) return; pts.Add(p); speeds.Add(v); }
                void Run(TownRoad road, float from, float to, int dir)
                {
                    float len = Mathf.Abs(to - from);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / 12f));
                    for (int k = 0; k <= steps; k++) Add(LanePoint(road, Mathf.Lerp(from, to, k / (float)steps), dir), LaneSpeed(road));
                }

                for (int step = 0; step < 14; step++)
                {
                    // The next thing ahead on this road.
                    TownJunction next = null; float nextAt = d > 0 ? float.MaxValue : float.MinValue;
                    foreach (var j in JunctionsOn(r))
                    {
                        float at = r.AlongX ? j.at.x : j.at.y;
                        if (d > 0 ? (at > s + 1f && at < nextAt) : (at < s - 1f && at > nextAt)) { next = j; nextAt = at; }
                    }
                    bool ring = TouchesRing(r, out int ringDir) && ringDir == d;
                    float ringAt = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                    if (ring && next != null && (d > 0 ? ringAt < nextAt : ringAt > nextAt)) next = null;

                    if (next == null && ring)
                    {
                        // Into the roundabout: give way at the line, round anticlockwise, out at another road.
                        float stopAt = ringAt - d * (RoundaboutWalk + 1.5f);
                        Run(r, s, stopAt, d);
                        stopI.Add(pts.Count - 1); stopR.Add(3); stopJ.Add(-1); stopX.Add((byte)(r.AlongX ? 1 : 0));
                        var ringRoads = _townRoads.Where(o => o != r && TouchesRing(o, out _)).ToList();
                        if (ringRoads.Count == 0) break;
                        var exit = ringRoads[rng.Next(ringRoads.Count)];
                        TouchesRing(exit, out int exitEnd);
                        int exitDir = -exitEnd;                       // driving away from the ring
                        Vector3 entry = LanePoint(r, ringAt - d * (RoundaboutOuter - 1f), d);
                        float exitRingAt = exit.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                        Vector3 exitPt = LanePoint(exit, exitRingAt + exitDir * (RoundaboutOuter - 1f), exitDir);
                        float a0 = Mathf.Atan2(entry.z - RoundaboutCentre.y, entry.x - RoundaboutCentre.x);
                        float a1 = Mathf.Atan2(exitPt.z - RoundaboutCentre.y, exitPt.x - RoundaboutCentre.x);
                        while (a1 <= a0 + 0.2f) a1 += Mathf.PI * 2f;
                        for (float a = a0; a < a1; a += 8f * Mathf.Deg2Rad)
                        {
                            float x = RoundaboutCentre.x + Mathf.Cos(a) * RingLane, z = RoundaboutCentre.y + Mathf.Sin(a) * RingLane;
                            Add(new Vector3(x, TownRoadY(x, z) + 0.02f, z), 7f);
                        }
                        r = exit; d = exitDir; s = exitRingAt + d * (RoundaboutWalk + 2f);
                        Add(LanePoint(r, s, d), 8f);
                        continue;
                    }
                    if (next == null)
                    {
                        // The end of the road: only the highway leaves the map; anything else ends the route here.
                        Run(r, s, d > 0 ? r.Max : r.Min, d);
                        break;
                    }

                    var other = next.x == r ? next.z : next.x;
                    float otherAt = other.AlongX ? next.at.x : next.at.y;
                    int rule = next.signals ? 2 : (Rank(r) < Rank(other) || (Rank(r) == Rank(other) && EndsAt(r, next.at))) ? 1 : 0;
                    float stopLine = nextAt - d * (other.Corridor + (next.signals ? 5f : 1.2f));
                    Run(r, s, stopLine, d);
                    if (rule != 0)
                    {
                        stopI.Add(pts.Count - 1); stopR.Add(rule);
                        stopJ.Add(rule == 2 && sigIndex.TryGetValue(next, out int si) ? si : -1);
                        stopX.Add((byte)(r.AlongX ? 1 : 0));
                    }

                    // Choose: straight on, or a turn onto the crossing road, never back, never into a dead end.
                    var options = new List<(TownRoad road, int dir)>();
                    if (Continues(r, nextAt, d) && Leads(r, nextAt, d)) options.Add((r, d));
                    foreach (int d2 in new[] { 1, -1 })
                        if (Continues(other, otherAt, d2) && Leads(other, otherAt, d2)) options.Add((other, d2));
                    if (options.Count == 0) break;
                    // Straight on is likelier than a turn.
                    var choice = options.Count > 1 && options[0].road == r && rng.NextDouble() < 0.5 ? options[0] : options[rng.Next(options.Count)];

                    if (choice.road == r)
                    {
                        s = nextAt + d * (other.Corridor + 1f);
                        Run(r, stopLine, s, d);
                    }
                    else
                    {
                        // A turn: a curve from this lane's stop line, through where the two lanes cross, to the new lane.
                        var a = pts[pts.Count - 1];
                        float exitAt = otherAt + choice.dir * (r.Corridor + 1f);
                        var b = LanePoint(choice.road, exitAt, choice.dir);
                        Vector3 cornerFrom = LanePoint(r, nextAt, d), cornerTo = LanePoint(choice.road, otherAt, choice.dir);
                        var ctrl = r.AlongX ? new Vector3(cornerTo.x, (a.y + b.y) * 0.5f, cornerFrom.z) : new Vector3(cornerFrom.x, (a.y + b.y) * 0.5f, cornerTo.z);
                        for (int k = 1; k <= 8; k++)
                        {
                            float t = k / 8f;
                            var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
                            Add(p, 5.5f);
                        }
                        r = choice.road; d = choice.dir; s = exitAt;
                    }
                }

                if (pts.Count < 6) continue;
                _routes.Add(new TrafficDirector.Route
                {
                    points = pts.ToArray(), speeds = speeds.ToArray(),
                    stopIndex = stopI.ToArray(), stopRule = stopR.ToArray(), stopJunction = stopJ.ToArray(), stopAlongX = stopX.ToArray(),
                });
            }
        }

        // ==================================================================
        // Civilians
        // ==================================================================
        /// <summary>
        /// A moving thing's renderers as the town lights them: shadows received, light and reflections from the probes
        /// blended where it stands (FPSKitTownProbes), so a civilian or a car takes the shade of a street as it walks or
        /// drives into it. Said outright rather than left to the packs' defaults; anything not on a lit URP shader is
        /// reported (VerifyTown checks the same).
        /// </summary>
        private static void DynamicLighting(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                r.receiveShadows = true;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name != "Universal Render Pipeline/Lit" && m.shader.name != "Universal Render Pipeline/Simple Lit")
                        Debug.LogWarning($"[FPSKit] town: {go.name}/{r.name} draws {m.name} with '{m.shader.name}', not a lit URP shader");
            }
        }

        private static GameObject[] CivilianPrefabs()
        {
            var controller = CivilianController();
            var list = new List<GameObject>();
            string[] idles = { "Idle/Idle_Breathing", "Idle/Idle_WeightShift", "Idle/Idle_LookAround", null };
            for (int i = 1; i <= 4; i++)
            {
                var src = PackPrefab($"Civilian_Man_0{i}");
                if (src == null) continue;
                string path = $"{FPSKitTownAssets.Out}/Civilian_0{i}.prefab";
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = $"Civilian_0{i}";
                var anim = go.GetComponentInChildren<Animator>();
                if (anim == null) { Object.DestroyImmediate(go); continue; }
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var clipIdle = idles[i - 1] != null ? MixamoClip(idles[i - 1]) : null;
                if (clipIdle != null)
                {
                    var ov = new AnimatorOverrideController(controller) { name = $"Civilian_0{i}" };
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    ov.GetOverrides(pairs);
                    for (int k = 0; k < pairs.Count; k++)
                        if (pairs[k].Key != null && pairs[k].Key.name.Contains("Idle")) pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[k].Key, clipIdle);
                    ov.ApplyOverrides(pairs);
                    string ovPath = $"{FPSKitTownAssets.Out}/Civilian_0{i}.overrideController";
                    AssetDatabase.DeleteAsset(ovPath);
                    AssetDatabase.CreateAsset(ov, ovPath);
                    anim.runtimeAnimatorController = ov;
                }
                else anim.runtimeAnimatorController = controller;

                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.32f; agent.height = 1.8f; agent.speed = 1.3f; agent.acceleration = 8f; agent.angularSpeed = 420f;
                agent.stoppingDistance = 0.6f; agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                agent.avoidancePriority = 60;
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.3f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f);
                var hp = go.AddComponent<Health>();
                hp.maxHealth = 100f;
                hp.destroyOnDeath = false;
                // Culled past about a hundred metres: a person further off is a few pixels, and a body is 40-120k triangles.
                var rends = go.GetComponentsInChildren<Renderer>();
                var lodg = go.AddComponent<LODGroup>();
                lodg.SetLODs(new[] { new LOD(0.012f, rends) });
                lodg.RecalculateBounds();
                var c = go.AddComponent<Civilian>();
                c.animator = anim;
                c.health = hp;
                SetLayerRecursive(go, LayerMask.NameToLayer("Civilian") >= 0 ? LayerMask.NameToLayer("Civilian") : 0);
                SetTagRecursive(go, "Flesh");
                DynamicLighting(go);
                list.Add(PrefabUtility.SaveAsPrefabAsset(go, path));
                Object.DestroyImmediate(go);
            }
            return list.ToArray();
        }

        private static AnimationClip MixamoClip(string file)
            => AssetDatabase.LoadAllAssetsAtPath($"{FPSKitMixamoImport.Root}{file}.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        private static AnimationClip StarterClip(string file)
            => AssetDatabase.LoadAllAssetsAtPath($"Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/{file}.anim.fbx")
                            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        /// <summary>
        /// The civilians' animator: a locomotion blend on Speed (stand, walk at 1.4 m/s, run at 4.8) and three deaths on
        /// the Die trigger picked by DieVariant. Regenerated each build so its clips follow the project.
        /// </summary>
        private static AnimatorController CivilianController()
        {
            string path = $"{FPSKitTownAssets.Out}/Civilian.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("DieVariant", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;
            var idle = StarterClip("Stand--Idle") ?? MixamoClip("Idle/Idle_Breathing");
            var walk = StarterClip("Locomotion--Walk_N");
            var run = StarterClip("Locomotion--Run_N");
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 1.4f);
            if (run != null) tree.AddChild(run, 4.8f);
            sm.defaultState = loco;
            string[] deaths = { "Deaths/Death_Back", "Deaths/Death_Front", "Deaths/Death_Right" };
            for (int i = 0; i < deaths.Length; i++)
            {
                var clip = MixamoClip(deaths[i]);
                if (clip == null) continue;
                var st = sm.AddState("Death" + i);
                st.motion = clip;
                var tr = sm.AddAnyStateTransition(st);
                tr.AddCondition(AnimatorConditionMode.If, 0f, "Die");
                tr.AddCondition(AnimatorConditionMode.Equals, i, "DieVariant");
                tr.duration = 0.12f;
                tr.canTransitionToSelf = false;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>Where people go: shop fronts, the souk, squares, the plazas, pavements of the main roads, the bus stop.</summary>
        private static List<Vector3> CivilianSpots()
        {
            var spots = new List<Vector3>();
            foreach (var f in _shopFronts) spots.Add(new Vector3(f.x, f.y, f.z));
            foreach (var c in _civicSpots) spots.Add(new Vector3(c.x, c.y, c.z) + new Vector3(2f, 0f, 2f));
            if (_soukStreet.width > 1f)
                for (float x = _soukStreet.xMin + 3f; x < _soukStreet.xMax - 3f; x += 7f)
                    spots.Add(new Vector3(x, SampleTownBase(x, 20f) + 0.2f, 20f + (x % 2f < 1f ? -1.5f : 1.5f)));
            for (float x = -50f; x <= 50f; x += 10f) spots.Add(new Vector3(x, SampleTownBase(x, 165f) + 0.25f, 165f));
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                var p = RoundaboutCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ((RoundaboutOuter + RoundaboutWalk) * 0.5f);
                if (!OnRoadMouth(p)) spots.Add(new Vector3(p.x, TownRoadY(p.x, p.y) + KerbHeight, p.y));
            }
            if (_busStop != Vector4.zero) spots.Add(new Vector3(_busStop.x, _busStop.y, _busStop.z));
            foreach (var r in _townRoads)
            {
                if (r.sidewalk <= 0f || !IsMain(r)) continue;
                for (float s = r.Min + 15f; s < r.Max - 15f; s += 24f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var p = RoadPoint(r, s, side * (r.Half + r.sidewalk * 0.5f), KerbHeight);
                        if (!InJunction(new Vector2(p.x, p.z))) spots.Add(p);
                    }
            }
            // A few out at the farms and the oasis landing.
            spots.Add(new Vector3(OasisCentre.x, _oasisWater + 0.3f, OasisCentre.y - OasisRadius + 1f));
            return spots;
        }
    }
}
#endif
