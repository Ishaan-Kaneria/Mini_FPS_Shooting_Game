#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's households, as data for <see cref="TownHouseholds"/> (Docs/DESERT_PEOPLE.md). While the houses are
    /// furnished, each piece that is used records where a person does something with it (<see cref="LifeSpot"/>: the sofa,
    /// the table's chairs, the burners and the sink, the bed, the majlis mattresses); here, after the buildings, those are
    /// gathered per house with a few standing spots in each room's middle (the phone, prayer, sweeping, talking), and
    /// every lit shop gets its counter and its shelves. No draws from any random sequence: the town does not reshuffle.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        struct LifeSpotMark
        {
            public Transform house;
            public LifeActivity kind;
            public Vector3 pos, approach;
            public float yaw;
        }

        /// <summary>Spots recorded while furnishing (cleared in BuildFromTheme).</summary>
        private static readonly List<LifeSpotMark> _lifeSpots = new List<LifeSpotMark>();

        /// <summary>The house mark being furnished (set in the interior builder, cleared in BuildFromTheme).</summary>
        private static Transform _lifeHouse;

        /// <summary>
        /// Records a spot for the house being furnished: the body at <paramref name="pos"/> facing <paramref name="facing"/>;
        /// the agent stops at <paramref name="approach"/> (the pos itself for something done standing).
        /// </summary>
        private static void LifeSpot(LifeActivity kind, Vector3 pos, Vector3 facing, Vector3? approach = null)
        {
            if (_lifeHouse == null) return;
            facing.y = 0f;
            float yaw = facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing).eulerAngles.y : 0f;
            _lifeSpots.Add(new LifeSpotMark { house = _lifeHouse, kind = kind, pos = pos, approach = approach ?? pos, yaw = yaw });
        }

        static void ClearLifeSpots()
        {
            _lifeSpots.Clear();
            _lifeHouse = null;
        }

        /// <summary>The households and the shopkeepers, on the town's life object, from the house and shop marks.</summary>
        private static void BuildHouseholds(Transform life, GameObject[] prefabs)
        {
            var spots = new List<TownHouseholds.Spot>();
            var houses = new List<TownHouseholds.House>();
            var shops = new List<TownHouseholds.Shop>();
            int seated = 0;

            if (_roomMarks != null)
                foreach (Transform h in _roomMarks)
                {
                    var door = h.Find("Door");
                    if (door == null) continue;
                    int first = spots.Count;
                    foreach (var s in _lifeSpots.Where(s => s.house == h))
                    {
                        spots.Add(new TownHouseholds.Spot { pos = s.pos, approach = s.approach, yaw = s.yaw, kind = s.kind });
                        if (LifeActivities.Seated(s.kind)) seated++;
                    }
                    // Standing things in each room's middle, by what the room is.
                    var centres = new List<Vector3>();
                    foreach (Transform room in h)
                    {
                        if (!room.name.StartsWith("Room ")) continue;
                        var look = room.Find("Look");
                        Vector3 at = (look != null ? look.position : room.position) - Vector3.up * 0.3f;
                        centres.Add(at);
                        string kind = room.name.Substring(5).Split(' ')[0];
                        float yaw = Hash01(at) * 360f;
                        void Stand(LifeActivity a, Vector3 offset) => spots.Add(new TownHouseholds.Spot { pos = at + offset, approach = at + offset, yaw = yaw, kind = a });
                        switch (kind)
                        {
                            case "living": Stand(LifeActivity.Phone, new Vector3(0.4f, 0f, 0f)); Stand(LifeActivity.Talk, new Vector3(-0.4f, 0f, 0.2f)); break;
                            case "majlis": Stand(LifeActivity.Pray, Vector3.zero); Stand(LifeActivity.Talk, new Vector3(0.5f, 0f, 0f)); break;
                            case "bedroom": Stand(LifeActivity.Pray, Vector3.zero); break;
                            case "kitchen":
                            case "oldkitchen": Stand(LifeActivity.Clean, Vector3.zero); break;
                            case "dining": Stand(LifeActivity.Clean, new Vector3(0f, 0f, 0.6f)); break;
                        }
                    }
                    int count = spots.Count - first;
                    if (count == 0 || centres.Count == 0) continue;
                    var centre = centres.Aggregate(Vector3.zero, (a, b) => a + b) / centres.Count;
                    // One to four people, more in a bigger house, from the house's place (no random draws).
                    int residents = Mathf.Clamp(1 + Mathf.FloorToInt(Hash01(door.position) * Mathf.Min(4, centres.Count + 1)), 1, 4);
                    houses.Add(new TownHouseholds.House { door = door.position, centre = centre, firstSpot = first, spotCount = count, residents = residents });
                }

            // Shops: a lit one gets its keeper at the back, a counter's customer, and the shelves along the sides.
            if (_shopMarks != null)
                foreach (Transform shop in _shopMarks)
                {
                    if (shop.name.EndsWith(" dark")) continue;
                    Vector3 P(float x, float z) => shop.TransformPoint(new Vector3(x, -0.5f, z)) + Vector3.up * 0.02f;
                    Vector3 fwd = shop.forward, right = shop.right;
                    int first = spots.Count;
                    void Add(LifeActivity a, Vector3 at, Vector3 face) =>
                        spots.Add(new TownHouseholds.Spot { pos = at, approach = at, yaw = Quaternion.LookRotation(new Vector3(face.x, 0f, face.z)).eulerAngles.y, kind = a });
                    Add(LifeActivity.Keeper, P(0f, -0.32f), fwd);
                    Add(LifeActivity.Pay, P(0f, -0.08f), -fwd);
                    Add(LifeActivity.Browse, P(-0.34f, -0.15f), -right);
                    Add(LifeActivity.Browse, P(0.34f, -0.15f), right);
                    Add(LifeActivity.Browse, P(-0.34f, 0.15f), -right);
                    Add(LifeActivity.Browse, P(0.34f, 0.15f), right);
                    shops.Add(new TownHouseholds.Shop { entrance = P(0f, 0.5f) + fwd * 1.2f, firstSpot = first, spotCount = spots.Count - first });
                }

            var town = life.gameObject.AddComponent<TownHouseholds>();
            town.prefabs = prefabs;
            town.houses = houses.ToArray();
            town.shops = shops.ToArray();
            town.spots = spots.ToArray();
            Debug.Log($"[FPSKit] households: {houses.Count} houses ({houses.Sum(h => h.residents)} people), {shops.Count} lit shops, " +
                      $"{spots.Count} spots ({seated} on furniture)");
        }

        static float Hash01(Vector3 p)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (int v in new[] { Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f) })
                {
                    h ^= (uint)v; h *= 16777619u; h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                }
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>
        /// The everyday states on the civilians' animator: an "Activity" int, and one state per LifeActivity whose clip is in
        /// Assets/Animations/Mixamo/Life/ (Life_&lt;Activity&gt;.fbx). Entered from any state while Activity is its number, left
        /// to the locomotion when it goes back to 0. An activity with no clip has no state: the body stands (or sits nowhere)
        /// in the locomotion's idle. Returns how many states were made.
        /// </summary>
        private static int AddLifeStates(AnimatorController ctrl, AnimatorState locomotion)
        {
            ctrl.AddParameter("Activity", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;
            int made = 0;
            foreach (LifeActivity a in System.Enum.GetValues(typeof(LifeActivity)))
            {
                if (a == LifeActivity.None) continue;
                var clip = MixamoClip("Life/" + LifeActivities.ClipName(a));
                if (clip == null) continue;
                var st = sm.AddState("Life_" + a);
                st.motion = clip;
                var into = sm.AddAnyStateTransition(st);
                into.AddCondition(AnimatorConditionMode.Equals, (int)a, "Activity");
                into.canTransitionToSelf = false;
                into.hasExitTime = false;
                into.duration = 0.35f;
                var back = st.AddTransition(locomotion);
                back.AddCondition(AnimatorConditionMode.NotEqual, (int)a, "Activity");
                back.hasExitTime = false;
                back.duration = 0.35f;
                made++;
            }
            return made;
        }
    }
}
#endif
