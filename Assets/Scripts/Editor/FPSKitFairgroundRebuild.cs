#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The realistic night fairground, as the Abandoned Fairground's arena.
    ///
    /// The art and layout come from the Fairground* generators (blockout, dressing, decals, lighting). This file is
    /// the seam between them and the game: it replaces <c>BuildParkZone</c> for that one arena and adds what a
    /// playable level needs that a dressed scene does not have:
    ///
    ///   * layers: solid things on Environment (enemy sight, the minimap), no-collider decoration on Backdrop
    ///     (kept out of the NavMesh bake and the minimap);
    ///   * a "Flood" NavMesh area with a higher cost over the lowlands, so enemies prefer dry ground and the
    ///     boardwalks, and a NavMesh seal outside the walls;
    ///   * zone spawn points in the order the fight travels (the Midway toward the Big Top) and the boss's anchor;
    ///   * zone outlines, a water footprint and two landmark icons for the minimap;
    ///   * the Low-tier switch (<see cref="FairgroundQuality"/>).
    ///
    /// On by default; <b>FPSKit &gt; Fairground &gt; Use Realistic Night Arena</b> switches back to the old park plan.
    /// The old plan's own checks (decks, walkways) are skipped while this is on, since they describe geometry this
    /// arena does not have.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        const string FairgroundThemeName = "Abandoned Fairground";
        const string RealisticPref = "FPSKit.FairgroundRealistic";
        const string NightProfilePath = "Assets/Fairground/Settings/FG_Night.asset";

        /// <summary>Zones in the order the fight moves through them: the level starts arrivals in the first and ends at the Big Top.</summary>
        static readonly string[] FairgroundFightOrder =
        {
            "The Midway", "North Fields", "Backlot", "West Meadow", "Roller Coaster", "Carousel Plaza", "Ferris Wheel", "Old Growth", "East Meadow", "South Meadow", "Big Top"
        };

        /// <summary>For the verify checks: whether the realistic arena is the one being built.</summary>
        public static bool RealisticFairgroundEnabled => EditorPrefs.GetBool(RealisticPref, true);

        static bool FairgroundRealisticActive => _theme != null && _theme.themeName == FairgroundThemeName && RealisticFairgroundEnabled;

        [MenuItem("FPSKit/Fairground/Use Realistic Night Arena")]
        static void ToggleRealisticFairground() => EditorPrefs.SetBool(RealisticPref, !RealisticFairgroundEnabled);

        [MenuItem("FPSKit/Fairground/Use Realistic Night Arena", true)]
        static bool ToggleRealisticFairgroundValidate()
        {
            Menu.SetChecked("FPSKit/Fairground/Use Realistic Night Arena", RealisticFairgroundEnabled);
            return true;
        }

        static string ZoneKey(string zoneName) => "Zone_" + zoneName.Replace(' ', '_');

        // ------------------------------------------------------------------ arena

        /// <summary>The arena step: everything the scene is made of, in the open scene the builder created.</summary>
        private static void BuildFairgroundRealistic()
        {
            ResetTerrain();                                  // the heightfield statics belong to the other arenas
            FairgroundDress.Generate();                      // the sites and their dressing, as planned
            Physics.SyncTransforms();
            FairgroundLighting.ApplyWorld();
            FairgroundDress.Finish(false);                   // then the whole fair is stretched out over the big arena

            var rootGo = GameObject.Find("Blockout");
            if (rootGo == null) { Debug.LogError("[FPSKit] The fairground generator produced no root."); return; }
            var root = rootGo.transform;

            EnsureFloodNavArea();
            SealNavMeshOutside(root, FairgroundBlockout.Half + 2f, FairgroundBlockout.Half + 80f);
            AssignFairgroundLayers(root);
            AddMinimapMarkers(root);
            AddQualityRig(root);

            var spawn = GameObject.Find("PlayerSpawn");
            if (spawn != null) _playerStart = spawn.transform.position + Vector3.up * 1f;

            rootGo.name = "Arena";                           // what every other arena's root is called
        }

        // ------------------------------------------------------------------ layers

        private static void AssignFairgroundLayers(Transform root)
        {
            int env = LayerMask.NameToLayer("Environment"), back = LayerMask.NameToLayer("Backdrop");
            if (env < 0 || back < 0) { Debug.LogWarning("[FPSKit] Environment/Backdrop layers are missing. Run EnsureProjectTagsAndLayers."); return; }

            int solid = 0, decor = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (go.GetComponent<Terrain>() != null) { go.layer = env; solid++; continue; }

                bool hasSolid = false;
                foreach (var c in go.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) { hasSolid = true; break; }
                if (hasSolid) { go.layer = env; solid++; continue; }

                bool drawn = go.GetComponent<Renderer>() != null || go.GetComponent<DecalProjector>() != null;
                if (drawn) { go.layer = back; decor++; }
            }
            Debug.Log($"[FPSKit] Fairground layers: {solid} solid on Environment, {decor} decorative on Backdrop.");
        }

        // ------------------------------------------------------------------ navmesh

        /// <summary>The project's NavMesh areas: slot 3 becomes "Flood", cost 2.5. Walkable, Not Walkable and Jump are untouched.</summary>
        private static void EnsureFloodNavArea()
        {
            if (NavMesh.GetAreaFromName("Flood") >= 0) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[FPSKit] Could not open NavMeshAreas.asset; the flood has no extra cost."); return; }

            var so = new SerializedObject(assets[0]);
            var areas = so.FindProperty("areas");
            if (areas == null) return;
            for (int i = 3; i < areas.arraySize; i++)
            {
                var element = areas.GetArrayElementAtIndex(i);
                var nameProp = element.FindPropertyRelative("name");
                if (nameProp == null || !string.IsNullOrEmpty(nameProp.stringValue)) continue;
                nameProp.stringValue = "Flood";
                var cost = element.FindPropertyRelative("cost");
                if (cost != null) cost.floatValue = 2.5f;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return;
            }
        }

        /// <summary>
        /// Marks the flooded ground as the Flood area. The box stops just above the water, so the boardwalks (a
        /// few centimetres above it) stay ordinary walkable ground and are the cheap way across.
        /// </summary>
        private static void AddFloodNavVolume(Transform root)
        {
            int area = NavMesh.GetAreaFromName("Flood");
            if (area < 0) return;
            var go = new GameObject("FloodNavVolume");
            go.transform.SetParent(root, false);
            // The flood is the ground below the waterline: the south-east quarter, from x=50, z=-64 out to the walls.
            float h = FairgroundBlockout.Half;
            go.transform.position = new Vector3((50f + h) * 0.5f, -0.575f, (-64f - h) * 0.5f);
            var v = go.AddComponent<NavMeshModifierVolume>();
            v.size = new Vector3(h - 50f, 1.25f, h - 64f);
            v.area = area;
        }

        // ------------------------------------------------------------------ minimap

        private static void AddMinimapMarkers(Transform root)
        {
            var group = new GameObject("MinimapMarkers").transform;
            group.SetParent(root, false);
            int back = Mathf.Max(0, LayerMask.NameToLayer("Backdrop"));

            // Muted, so the outlines frame the map without competing with enemies or the player.
            var tones = new Dictionary<string, (Color color, string label)>
            {
                ["Main Gate"] = (new Color(0.95f, 0.80f, 0.35f, 0.55f), "GATE"),
                ["The Midway"] = (new Color(0.95f, 0.62f, 0.32f, 0.55f), "MIDWAY"),
                ["Carousel Plaza"] = (new Color(0.50f, 0.80f, 0.56f, 0.55f), "CAROUSEL"),
                ["Ferris Wheel"] = (new Color(0.50f, 0.75f, 0.95f, 0.55f), ""),            // the icon names it
                ["Old Growth"] = (new Color(0.36f, 0.72f, 0.42f, 0.55f), "WOODS"),
                ["Roller Coaster"] = (new Color(0.74f, 0.56f, 0.92f, 0.55f), "COASTER"),
                ["Backlot"] = (new Color(0.88f, 0.48f, 0.62f, 0.55f), "BACKLOT"),
                ["Big Top"] = (new Color(0.95f, 0.42f, 0.42f, 0.55f), ""),
                ["West Meadow"] = (new Color(0.58f, 0.82f, 0.44f, 0.40f), "MEADOW"),
                ["North Fields"] = (new Color(0.58f, 0.82f, 0.44f, 0.40f), "FIELDS"),
                ["South Meadow"] = (new Color(0.58f, 0.82f, 0.44f, 0.40f), "MEADOW"),
                ["East Meadow"] = (new Color(0.58f, 0.82f, 0.44f, 0.40f), "MEADOW"),                 // so does this one
            };
            foreach (var z in FairgroundBlockout.ZoneDefs)
            {
                if (!tones.TryGetValue(z.name, out var tone)) continue;
                var go = new GameObject("MapZone_" + z.name.Replace(' ', '_'));
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(z.centre.x, 0f, z.centre.y);
                var m = go.AddComponent<MinimapMarker>();
                m.style = MinimapMarker.Style.Outline;
                m.size = z.size * 0.94f;
                m.color = tone.color;
                m.label = tone.label;
            }

            // The old-growth wood: a dark green footprint under everything else. The quad is never drawn, only measured.
            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "MapWoods";
            water.transform.SetParent(group, false);
            float hh = FairgroundBlockout.Half;
            water.transform.position = new Vector3((50f + hh) * 0.5f, -0.3f, (-64f - hh) * 0.5f);
            water.transform.localScale = new Vector3(hh - 50f, 0.2f, hh - 64f);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.GetComponent<MeshRenderer>().forceRenderingOff = true;
            water.layer = back;
            var wm = water.AddComponent<MinimapMarker>();
            wm.style = MinimapMarker.Style.Footprint;
            wm.color = new Color(0.10f, 0.24f, 0.13f, 0.9f);
            wm.order = -3;

            Icon(group, "MapIcon_Ferris", FairgroundBlockout.FerrisCentre, new Color(0.96f, 0.80f, 0.36f), "WHEEL", 3.2f);
            Icon(group, "MapIcon_BigTop", FairgroundBlockout.BigTopCentre, new Color(0.96f, 0.36f, 0.32f), "BIG TOP", 3.6f);
        }

        private static void Icon(Transform parent, string name, Vector2 at, Color color, string label, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(at.x, 0f, at.y);
            var m = go.AddComponent<MinimapMarker>();
            m.style = MinimapMarker.Style.Icon;
            m.color = color; m.label = label; m.dotRadius = radius; m.order = 5;
        }

        // ------------------------------------------------------------------ quality

        private static void AddQualityRig(Transform root)
        {
            var go = new GameObject("FairgroundQuality");
            var q = go.AddComponent<FairgroundQuality>();
            var all = root.GetComponentsInChildren<Transform>(true);

            var rain = all.FirstOrDefault(t => t.name == "Rain");
            q.rain = rain != null ? rain.gameObject : null;
            q.mistCards = all.Where(t => t.name == "MistCard").Select(t => t.GetComponent<Renderer>()).Where(r => r != null).ToArray();
            q.lightShafts = all.Where(t => t.name.EndsWith("Shaft") || t.name == "WorkLightBeam").Select(t => t.GetComponent<Renderer>()).Where(r => r != null).ToArray();
            q.minorLights = all.Where(t => t.name == "BulbLight" || t.name == "CampFireLight").Select(t => t.GetComponent<Light>()).Where(l => l != null).ToArray();
            var flood = all.FirstOrDefault(t => t.name == "FloodWater");
            q.water = flood != null && flood.GetComponent<Renderer>() != null ? new[] { flood.GetComponent<Renderer>() } : new Renderer[0];
        }

        // ------------------------------------------------------------------ spawns

        /// <summary>
        /// Spawn points, grouped by zone in the order the fight travels. Each zone gets up to eight, well apart, on
        /// open ground (nothing solid within a metre) and not deeper in the flood than wading. They are pulled onto the
        /// NavMesh once it is baked, like every other arena's.
        /// </summary>
        private static Transform[] BuildFairgroundSpawnPoints()
        {
            Physics.SyncTransforms();
            var root = new GameObject("SpawnPoints").transform;
            var all = new List<Transform>();
            var rng = new System.Random(1962);

            foreach (string zoneName in FairgroundFightOrder)
            {
                var def = FairgroundBlockout.ZoneDefs.FirstOrDefault(z => z.name == zoneName);
                if (def.name == null) continue;

                var zone = new GameObject(ZoneKey(zoneName)).transform;
                zone.SetParent(root, false);
                var made = new List<Vector3>();

                for (int tries = 0; tries < 600 && made.Count < 8; tries++)
                {
                    float x = def.centre.x + ((float)rng.NextDouble() - 0.5f) * def.size.x * 0.9f;
                    float z = def.centre.y + ((float)rng.NextDouble() - 0.5f) * def.size.y * 0.9f;
                    float y = FairgroundBlockout.H(x, z);
                    if (y < -0.7f || Mathf.Abs(x) > FairgroundBlockout.Half - 4f || Mathf.Abs(z) > FairgroundBlockout.Half - 4f) continue;

                    var p = new Vector3(x, y, z);
                    if (made.Any(o => (o - p).sqrMagnitude < 36f)) continue;

                    var hits = Physics.OverlapSphere(p + Vector3.up * 1f, 1.1f, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => !(h is TerrainCollider))) continue;

                    made.Add(p);
                    var point = new GameObject("Spawn_" + made.Count).transform;
                    point.SetParent(zone, false);
                    point.position = p + Vector3.up * 0.1f;
                    all.Add(point);
                }

                if (made.Count == 0) Debug.LogWarning($"[FPSKit] No spawn points found in {zoneName}.");
            }

            return all.ToArray();
        }

        /// <summary>The level manager's half of the fairground: zones, the boss's place, a ring that fits 450 metres.</summary>
        private static void WireFairgroundLevelManager(LevelManager manager)
        {
            var root = GameObject.Find("SpawnPoints");
            if (root != null)
            {
                manager.spawnZones = FairgroundFightOrder.Select(n =>
                {
                    var zone = root.transform.Find(ZoneKey(n));
                    return new LevelManager.SpawnZone
                    {
                        name = n,
                        points = zone != null ? zone.Cast<Transform>().ToArray() : new Transform[0]
                    };
                }).ToArray();
            }

            var boss = GameObject.Find("BossSpawn");
            manager.bossSpawnPoint = boss != null ? boss.transform : null;

            // 411 m was the old park (a 700 m arena); this one is 180 m square. The ring is the fallback now, and it
            // is capped so no level's reach can stretch it past the arena.
            manager.minSpawnDistanceFromPlayer = 18f;
            manager.maxSpawnDistanceFromPlayer = 70f;
            manager.spawnRingCap = 170f;
            manager.zoneSpawnDistance = new Vector2(18f, 260f);

            // The walls are the boundary, so the leash is only a safety net for falls and the NavMesh check: it sits
            // beyond the arena's diagonal (255 m), because the boss stands 155 m from where the player starts.
            manager.despawnDistance = 560f;
        }

        // ------------------------------------------------------------------ hooks the builder calls

        private static void FairgroundAfterPlayer(GameObject player)
        {
            if (player == null) return;
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);          // out of the gate, facing the fair
            var cam = player.GetComponentInChildren<Camera>();
            if (cam != null) cam.farClipPlane = Mathf.Max(cam.farClipPlane, 320f);
        }

        private static VolumeProfile FairgroundVolumeProfile()
            => FairgroundVolume.Night();          // rebuilt every time, so the values stay in code
    }
}
#endif
