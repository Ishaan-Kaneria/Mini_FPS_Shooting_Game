#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town (LevelTheme.desertTownZone): Desert Outpost rebuilt from scratch as a lived-in Middle East
    /// town, a kilometre square, in golden afternoon light (the user's brief, 2026-10-20).
    ///
    /// Built in this order, each pass reading what the passes before it left:
    ///   plan        FPSKitTownPlan       roads, junctions, districts, landmark sites, farm plots
    ///   ground      FPSKitTownGround     terrain heights, roads carved in, ground paint, sky, horizon, edge
    ///   roads       FPSKitTownRoads      asphalt, pavements, kerbs, median, markings, roundabout
    ///   buildings   FPSKitTownBuildings  houses, apartment blocks with shops, the old town, interiors
    ///   landmarks   FPSKitTownLandmarks  mosque, citadel, palace, clock tower and bazaar gate, souk
    ///   edge        FPSKitTownEdge       gas station, checkpoint, garage, bus stop, junkyard, water tower, farms, oasis
    ///   street      FPSKitTownStreet     lamps, signals, signs, poles and wires, furniture, palms, clutter
    ///   life        FPSKitTownLife       parked cars, traffic routes, civilian spots and the directors that use them
    ///
    /// The art is real where it can be: Poly Haven CC0 photo-scans for ground, walls and props, the user's car,
    /// palm and civilian packs (URP copies, FPSKitTownAssets), and the Mediterranean kit's doors and arches.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>Footprints of everything solid that is not to be walked into (x0, z0, x1, z1), for spawns and clutter.</summary>
        private static readonly List<Rect> _townSolids = new List<Rect>();

        /// <summary>Farm plots inside FarmsRect, planned before the ground so the soil can be painted.</summary>
        private static readonly List<Rect> _farmPlots = new List<Rect>();

        /// <summary>
        /// Box-projected reflection probes down the town's main streets (the avenue, north and south, and the boulevard),
        /// rendered once when the level loads: windows and car paint reflect the street they face, not only the sky.
        /// Realtime and refreshed on awake rather than baked, because the scene is rebuilt from code (a bake would have
        /// to follow every build) and it needs no lightmapping pass. 128 per face; the phone copy removes them.
        /// </summary>
        private static void PlaceStreetProbes(Transform root)
        {
            var group = new GameObject("StreetProbes").transform;
            group.SetParent(root, false);
            int n = 0;
            void Probe(Vector3 at, Vector3 box)
            {
                var go = new GameObject("StreetProbe");
                go.transform.SetParent(group, false);
                go.transform.position = at;
                var p = go.AddComponent<ReflectionProbe>();
                p.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
                p.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
                p.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                p.resolution = 128;
                p.hdr = true;
                p.boxProjection = true;
                p.size = box;
                p.center = Vector3.zero;
                p.blendDistance = 6f;
                p.farClipPlane = 220f;
                p.shadowDistance = 0f;
                p.cullingMask = ~LayerMask.GetMask("Civilian", "Enemy", "Player", "FirstPerson", "PlayerBounds");
                n++;
            }
            // Every 150 m down the avenue (x = 0) and the boulevard (z = 20), the box spanning street and pavements to
            // the building fronts either side and as high as the blocks.
            for (float z = -330f; z <= 170f; z += 150f) Probe(new Vector3(0f, GroundHeightAt(0f, z) + 4f, z), new Vector3(52f, 36f, 150f));
            for (float x = -300f; x <= 300f; x += 150f)
                if (Mathf.Abs(x) > 40f) Probe(new Vector3(x, GroundHeightAt(x, 20f) + 4f, 20f), new Vector3(150f, 36f, 46f));
            Debug.Log($"[FPSKit] town: {n} street reflection probes");
        }

        private static void ResetTown()
        {
            _townRoads.Clear(); _townTracks.Clear(); _townSites.Clear(); _townJunctions.Clear();
            _townSolids.Clear(); _farmPlots.Clear(); _townMats.Clear(); _townZones.Clear();
            _townBase = null; _devDist = null; _townTerrain = null; _townSkyRotation = float.NaN;
            ResetTownBuildings();
            ResetTownLife();
        }

        private static void BuildDesertTown()
        {
            _claimed.Clear();
            _packCache.Clear();
            _packMissing.Clear();
            ClearMeshPool();
            ResetTerrain();
            ResetTown();

            FPSKitTownAssets.Prepare();

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;
            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string what) { Debug.Log($"[FPSKit] town: {what} ({clock.Elapsed.TotalSeconds:0.0}s)"); }

            PlanTown();
            PlanFarms();
            FindJunctions();
            Lap("planned");

            BuildTownGround(root, layer, backdrop);
            Lap("ground");
            BuildTownRoads(root, layer);
            Lap("roads");
            BuildTownBuildings(root, layer, rng);
            Lap("buildings");
            BuildTownLandmarks(root, layer, rng);
            Lap("landmarks");
            BuildTownEdge(root, layer, rng);
            Lap("edge");
            BuildTownStreet(root, layer, rng);
            Lap("street");
            BuildTownLife(root, layer, rng);
            Lap("life");
            PlaceStreetProbes(root);

            // Nothing past the edge is anybody's to stand on.
            SealNavMeshOutside(root, half - 2f, half + 700f);

            _playerStart = new Vector3(TownStart.x, TownRoadY(TownStart.x, TownStart.z) + KerbHeight + 0.15f, TownStart.z);
            Debug.Log($"[FPSKit] desert town built: {_townRoads.Count} roads, {_townJunctions.Count} junctions, " +
                      $"{_townSolids.Count} solid footprints, {_farmPlots.Count} farm plots in {clock.Elapsed.TotalSeconds:0}s.");
        }

        // ==================================================================
        // Farms
        // ==================================================================
        /// <summary>
        /// Palm groves in walled plots round the oasis: a grid of plots with paths between, none in the water or on
        /// its cracked shore, none across the farm track.
        /// </summary>
        private static void PlanFarms()
        {
            _farmPlots.Clear();
            const float w = 46f, d = 34f, path = 6f;
            for (float x = FarmsRect.xMin + 4f; x + w <= FarmsRect.xMax - 2f; x += w + path)
                for (float z = FarmsRect.yMin + 4f; z + d <= FarmsRect.yMax - 4f; z += d + path)
                {
                    var r = new Rect(x, z, w, d);
                    var near = new Vector2(Mathf.Clamp(OasisCentre.x, r.xMin, r.xMax), Mathf.Clamp(OasisCentre.y, r.yMin, r.yMax));
                    if (Vector2.Distance(near, OasisCentre) < OasisRadius + 22f) continue;
                    _farmPlots.Add(r);
                }
        }

        /// <summary>1 inside a farm plot's irrigated bed, 0 on its paths and walls, with a soft edge.</summary>
        private static float FarmSoilAt(float x, float z)
        {
            foreach (var r in _farmPlots)
            {
                if (x < r.xMin || x > r.xMax || z < r.yMin || z > r.yMax) continue;
                float inset = Mathf.Min(x - r.xMin, r.xMax - x, z - r.yMin, r.yMax - z);
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((inset - 1.2f) / 1.5f));
            }
            return 0f;
        }

        // ==================================================================
        // Spawns
        // ==================================================================
        /// <summary>The town's spawn zones, nearest the player's start first. Filled by BuildTownSpawnPoints.</summary>
        private static readonly List<(string name, Transform[] points)> _townZones = new List<(string, Transform[])>();

        /// <summary>
        /// Enemies come from the town's places -- the famous ones the levels will be built round, and the districts
        /// between them. Each point is on open ground: a ray from above lands on the terrain, a road or a pavement
        /// (never a roof), nothing solid stands where a body would, and it is outside every building's footprint.
        /// </summary>
        private static Transform[] BuildTownSpawnPoints()
        {
            _townZones.Clear();
            Physics.SyncTransforms();
            var root = new GameObject("SpawnPoints").transform;
            var rng = new System.Random(7731);
            float half = _theme.arenaSize * 0.5f;

            var places = new List<(string name, Vector2 at, float radius, int count)>
            {
                ("ClockSquare", RoundaboutCentre, 44f, 6),
                ("Souk", new Vector2(-130f, 20f), 60f, 8),
                ("GrandMosque", MosqueRect.center, 75f, 8),
                ("Citadel", CitadelCentre, 62f, 8),
                ("Palace", PalaceRect.center, 70f, 8),
                ("GasStation", GasStationRect.center, 40f, 5),
                ("Checkpoint", CheckpointAt, 34f, 5),
                ("BusStop", BusStopAt, 30f, 4),
                ("Garage", GarageRect.center, 30f, 4),
                ("Junkyard", JunkyardRect.center, 55f, 6),
                ("WaterTower", WaterTowerAt, 34f, 5),
                ("Oasis", OasisCentre, 80f, 6),
                ("Farms", new Vector2(-250f, 420f), 90f, 6),
                ("OldTownWest", OldTownWestRect.center, 80f, 6),
                ("EastResidential", new Vector2(230f, -60f), 110f, 8),
                ("NorthResidential", new Vector2(280f, 150f), 80f, 6),
                ("SouthWest", new Vector2(-300f, -270f), 100f, 7),
            };

            var start = new Vector2(TownStart.x, TownStart.z);
            var all = new List<Transform>();
            foreach (var place in places.OrderBy(p => (p.at - start).sqrMagnitude))
            {
                var made = new List<Vector3>();
                for (int tries = 0; tries < 600 && made.Count < place.count; tries++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float rr = place.radius * Mathf.Sqrt((float)rng.NextDouble());
                    var p = place.at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (Mathf.Abs(p.x) > half - 12f || Mathf.Abs(p.y) > half - 12f) continue;
                    if ((p - start).sqrMagnitude < 40f * 40f) continue;
                    if (InSolid(p, 1.2f)) continue;
                    if (Vector2.Distance(p, OasisCentre) < OasisRadius + 2f) continue;
                    if (!Physics.Raycast(new Vector3(p.x, 400f, p.y), Vector3.down, out var hit, 800f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    bool ground = hit.collider is TerrainCollider || hit.collider.name.StartsWith("Road") ||
                                  hit.collider.name.Contains("Pavement") || hit.collider.name.Contains("Floor") ||
                                  hit.collider.name.Contains("Plaza") || hit.collider.name.Contains("Roundabout") ||
                                  hit.collider.name.Contains("Walk") || hit.collider.name.Contains("Yard");
                    if (!ground || Mathf.Abs(hit.point.y - GroundHeightAt(p.x, p.y)) > 1.2f && !place.name.Equals("Citadel")) continue;
                    var at = hit.point + Vector3.up * 0.1f;
                    if (made.Any(m => (m - at).sqrMagnitude < 8f * 8f)) continue;
                    bool blocked = Physics.OverlapSphere(at + Vector3.up * 1.1f, 0.8f, ~0, QueryTriggerInteraction.Ignore)
                                          .Any(c => !(c is TerrainCollider) && c.gameObject.layer != LayerMask.NameToLayer("PlayerBounds"));
                    if (blocked) continue;
                    made.Add(at);
                }
                if (made.Count == 0) { Debug.LogWarning($"[FPSKit] town spawn zone {place.name} found no open ground"); continue; }

                var zone = new GameObject("Zone_" + place.name).transform;
                zone.SetParent(root, false);
                var points = new List<Transform>();
                for (int i = 0; i < made.Count; i++)
                {
                    var t = new GameObject($"Spawn_{place.name}_{i}").transform;
                    t.SetParent(zone, false);
                    t.position = made[i];
                    points.Add(t);
                }
                _townZones.Add((place.name, points.ToArray()));
                all.AddRange(points);
            }
            Debug.Log($"[FPSKit] town spawns: {all.Count} points in {_townZones.Count} zones (" +
                      string.Join(", ", _townZones.Select(z => $"{z.name} {z.points.Length}")) + ")");
            return all.ToArray();
        }

        /// <summary>The zones in order, and a leash that clears the points' real spread (as the old desert did).</summary>
        private static void WireTownLevelManager(LevelManager manager, Transform[] points)
        {
            if (_townZones.Count == 0) return;
            manager.spawnZones = _townZones.Select(z => new LevelManager.SpawnZone { name = z.name, points = z.points }).ToArray();
            manager.zoneSpawnDistance = new Vector2(22f, 260f);
            float span = 0f;
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                    if (points[i] != null && points[j] != null)
                        span = Mathf.Max(span, Vector3.Distance(points[i].position, points[j].position));
            manager.despawnDistance = Mathf.Max(manager.despawnDistance, Mathf.Ceil((span + 40f) / 10f) * 10f);
        }

        /// <summary>Whether a ground point is inside (or within <paramref name="margin"/> of) any solid footprint.</summary>
        private static bool InSolid(Vector2 p, float margin = 0f)
        {
            foreach (var r in _townSolids)
                if (p.x > r.xMin - margin && p.x < r.xMax + margin && p.y > r.yMin - margin && p.y < r.yMax + margin) return true;
            return false;
        }

        /// <summary>Whether a rectangle overlaps any solid footprint.</summary>
        private static bool OverlapsSolid(Rect r, float margin = 0f)
        {
            var g = Rect.MinMaxRect(r.xMin - margin, r.yMin - margin, r.xMax + margin, r.yMax + margin);
            foreach (var s in _townSolids) if (s.Overlaps(g)) return true;
            return false;
        }

        // ==================================================================
        // Placing prefabs
        // ==================================================================
        private static readonly Dictionary<string, GameObject> _townPrefabs = new Dictionary<string, GameObject>();

        /// <summary>A Poly Haven prop prefab by its id (FPSKitTownAssets), or null if it was not fetched.</summary>
        private static GameObject Prop(string id) => TownPrefab($"{FPSKitTownAssets.PropFolder}/{id}.prefab");

        /// <summary>A store-pack prefab by its town name (FPSKitTownAssets.PackPrefabs), or null if the pack is missing.</summary>
        private static GameObject PackPrefab(string name) => TownPrefab($"{FPSKitTownAssets.PackFolder}/{name}.prefab");

        private static GameObject TownPrefab(string path)
        {
            if (_townPrefabs.TryGetValue(path, out var p)) return p;
            p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            _townPrefabs[path] = p;
            return p;
        }

        /// <summary>
        /// Puts a prefab down with its base on <paramref name="pos"/>, turned by <paramref name="yaw"/>, optionally
        /// scaled so it stands <paramref name="height"/> metres tall. Measured from its renderers, so it works for any
        /// pivot. Tagged for impacts by what it is made of, kept off the navigation bake's walkable surfaces.
        /// </summary>
        private static GameObject PlaceTown(Transform parent, GameObject prefab, Vector3 pos, float yaw, float height = -1f,
                                            float scale = 1f, string surface = null, bool navBlock = true)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = Vector3.zero;
            var b = WorldBounds(go);
            if (height > 0f && b.size.y > 0.01f)
            {
                go.transform.localScale *= height / b.size.y;
                b = WorldBounds(go);
            }
            go.transform.position = pos + Vector3.up * -b.min.y;
            SetLayerRecursive(go, LayerMask.NameToLayer("Environment"));
            string tag = surface ?? SurfaceFor(prefab.name);
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.gameObject.tag = tag;
            if (navBlock) NoStanding(go);
            else
            {
                var mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                mod.ignoreFromBuild = true;
            }
            return go;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static string SurfaceFor(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("wood") || n.Contains("crate") || n.Contains("table") || n.Contains("chair") || n.Contains("stool") ||
                n.Contains("shelf") || n.Contains("shelves") || n.Contains("ladder") || n.Contains("basket") || n.Contains("bench") ||
                n.Contains("cabinet") || n.Contains("bookshelf") || n.Contains("palm") || n.Contains("trunk") || n.Contains("branch") ||
                n.Contains("bed")) return "Wood";
            if (n.Contains("barrel") || n.Contains("tank") || n.Contains("lamp") || n.Contains("pole") || n.Contains("utility") ||
                n.Contains("aircon") || n.Contains("car") || n.Contains("can") || n.Contains("generator") || n.Contains("rim") ||
                n.Contains("register") || n.Contains("truck") || n.Contains("shutter") || n.Contains("fence") || n.Contains("brass") ||
                n.Contains("gun") || n.Contains("searchlight") || n.Contains("camera") || n.Contains("light") || n.Contains("frame"))
                return "Metal";
            return "Concrete";
        }
    }
}
#endif
