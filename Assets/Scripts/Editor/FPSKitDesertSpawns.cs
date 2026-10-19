#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Where Desert Outpost's enemies come from (combat audit C2, 2026-10-19): round its places -- the base, the town,
    /// the hamlets, the farms, the camps, the watch posts -- rather than off a ring of twelve points that ran almost
    /// to the arena's edge. That ring spanned 324 m, so LevelManager forced the leash up from 158 m to clear it and
    /// warned on every level start ("Despawn Distance (158m) is too close to the spawn ring or spawn points (324m)").
    ///
    /// Done as the Fairground does it: zones of points, nearest place to the player's start first, so the fight
    /// travels out across the map as the roster is spent; each point on open ground at the edge of its place, out of
    /// the river, inside the arena; out of the player's view is LevelManager's own test when it uses one. The leash
    /// is then set from the points' real spread, so nothing is culled on arrival and nothing is warned about.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>The desert's zones, nearest the player's start first. Filled by BuildDesertSpawnPoints.</summary>
        private static readonly List<(string name, Transform[] points)> _desertZones = new List<(string, Transform[])>();

        private static Transform[] BuildDesertSpawnPoints()
        {
            _desertZones.Clear();
            Physics.SyncTransforms();
            var root = new GameObject("SpawnPoints").transform;
            var rng = new System.Random(4501);
            float half = _theme.arenaSize * 0.5f;
            float river = _theme.hazardWidth * 0.5f + RimBandWidth + 8f;

            // Every place, with the radius its points stand round and how many it gets.
            var places = new List<(string name, Vector2 at, float radius, int count)>();
            if (_hasFob) places.Add(("Base", _fob.Centre, _fob.HalfWorld.magnitude + 12f, 8));
            if (_hasTown) places.Add(("Town", _townCentre, _townRadius + 10f, 12));
            for (int i = 0; i < _hamletPlans.Count; i++) places.Add(($"Hamlet{i + 1}", _hamletPlans[i].Centre, _hamletPlans[i].HalfWorld.magnitude + 10f, 6));
            for (int i = 0; i < _farmPlans.Count; i++) places.Add(($"Farm{i + 1}", _farmPlans[i].Centre, _farmPlans[i].HalfWorld.magnitude + 10f, 5));
            for (int i = 0; i < _campSites.Count; i++) places.Add(($"Camp{i + 1}", _campSites[i], 16f, 4));
            for (int i = 0; i < _watchPlans.Count; i++) places.Add(($"Post{i + 1}", _watchPlans[i], 16f, 4));

            var player = GameObject.FindWithTag("Player");
            Vector2 start = player != null ? new Vector2(player.transform.position.x, player.transform.position.z) : Vector2.zero;

            var all = new List<Transform>();
            foreach (var place in places.OrderBy(p => (p.at - start).sqrMagnitude))
            {
                var zone = new GameObject("Zone_" + place.name).transform;
                zone.SetParent(root, false);
                var made = new List<Vector3>();
                for (int tries = 0; tries < 240 && made.Count < place.count; tries++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                    float r = place.radius + (float)rng.NextDouble() * 10f;
                    var p = place.at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (Mathf.Abs(p.x) > half - 14f || Mathf.Abs(p.y) > half - 14f) continue;
                    if (Mathf.Abs(p.x - GorgeCentreAt(p.y)) < river) continue;
                    var at = new Vector3(p.x, GroundHeightAt(p.x, p.y) + 0.1f, p.y);
                    if (made.Any(m => (m - at).sqrMagnitude < 7f * 7f)) continue;
                    // Open ground: nothing solid where a body would stand.
                    bool blocked = Physics.OverlapSphere(at + Vector3.up * 1.1f, 0.9f, ~0, QueryTriggerInteraction.Ignore)
                                          .Any(c => !(c is TerrainCollider));
                    if (blocked) continue;
                    made.Add(at);
                }
                if (made.Count == 0) continue;

                var points = new List<Transform>();
                for (int i = 0; i < made.Count; i++)
                {
                    var t = new GameObject($"Spawn_{place.name}_{i}").transform;
                    t.SetParent(zone, false);
                    t.position = made[i];
                    points.Add(t);
                }
                _desertZones.Add((place.name, points.ToArray()));
                all.AddRange(points);
            }

            Debug.Log($"[FPSKit] desert spawns: {all.Count} points in {_desertZones.Count} zones (" +
                      string.Join(", ", _desertZones.Select(z => $"{z.name} {z.points.Length}")) + ")");
            return all.ToArray();
        }

        /// <summary>The level manager's half: the zones in order, and a leash that clears the points' real spread.</summary>
        private static void WireDesertLevelManager(LevelManager manager, Transform[] points)
        {
            if (_desertZones.Count == 0) return;
            manager.spawnZones = _desertZones.Select(z => new LevelManager.SpawnZone { name = z.name, points = z.points }).ToArray();
            manager.zoneSpawnDistance = new Vector2(18f, 240f);

            // The widest gap between two points is the farthest an enemy can start from the player; the leash sits 40 m
            // beyond it (LevelManager would otherwise raise it to the gap plus 20 and warn).
            float span = 0f;
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                    if (points[i] != null && points[j] != null)
                        span = Mathf.Max(span, Vector3.Distance(points[i].position, points[j].position));
            manager.despawnDistance = Mathf.Max(manager.despawnDistance, Mathf.Ceil((span + 40f) / 10f) * 10f);
        }
    }
}
#endif
