#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The cloud session's features measured in the real desert with no one at the keys: residents at home, a drum burst, a driver
    /// shot through the glass, a car meeting the player, punches on a wall, and what the glossy places reflect. Numbers in
    /// Build/CloudCheck/report.txt, frames beside it.
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitCloudCheck.Batch [-fpskitLevel 2]
    /// </summary>
    public static class FPSKitCloudCheck
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";
        const string Folder = "Build/CloudCheck";
        const float Step = 1f / 30f;
        static readonly StringBuilder Log = new StringBuilder();
        static System.Collections.IEnumerator _script;
        static PlayerMotor _motor;
        static Health _health;
        static MeleeStrike _melee;
        static Camera _cam;
        static int _lastFrame = -1, _level = -1, _wait;
        static bool _heal = true;
        static float _deadline;
        static Vector3 _start;

        public static void Batch()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-fpskitLevel");
            _level = i >= 0 && i + 1 < a.Length ? int.Parse(a[i + 1]) : -1;
            Directory.CreateDirectory(Folder);
            Log.Clear();
            EditorSceneManager.OpenScene(Scene);
            if (_level >= 0) foreach (var lm in Object.FindObjectsByType<LevelManager>(FindObjectsSortMode.None)) lm.defaultLevelIndex = _level;
            FPSKitPlayMode.SuspendStartScene();
            _script = null; _motor = null; _deadline = -1f;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static int _errors;
        static void CountRed(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) _errors++; }
        static void Note(string s) { Log.Append(s).Append('\n'); Debug.Log("[CloudCheck] " + s); }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (_motor == null)
                {
                    _motor = Object.FindAnyObjectByType<PlayerMotor>();
                    if (_motor == null) return;
                    _health = _motor.GetComponent<Health>();
                    _melee = _motor.GetComponent<MeleeStrike>();
                    Time.captureDeltaTime = Step;
                    MobileInput.Active = true;
                    _script = _worldRun ? WorldScript() : _carRun ? CarScript() : _level >= 0 ? LevelScript() : Script();
                    _deadline = Time.realtimeSinceStartup + 1100f;
                }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                if (_heal && _health != null && _health.Normalized < 0.9f) _health.ResetHealth();
                if (Time.realtimeSinceStartup > _deadline) { Note("TIMED OUT"); Finish(1); return; }
                if (_wait > 0) { _wait--; if (_wait % 2 == 0) Meter(); return; }
                if (!_script.MoveNext()) Finish(0);
                else if (_script.Current is float s) _wait = Mathf.RoundToInt(s / Step);
            }
            catch (System.Exception e) { Note("EXCEPTION " + e); Finish(1); }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText($"{Folder}/report{(_worldRun ? "_world" : _carRun ? "_car" : _level >= 0 ? "_level" + _level : "")}.txt", Log.ToString());
            Time.captureDeltaTime = 0f;
            MobileInput.Active = false;
            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.Exit(code);
        }

        static void Meter()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            var rt = RenderTexture.GetTemporary(480, 270, 24, RenderTextureFormat.ARGB32);
            try { UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_cam, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt }); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        static void Shot(string name)
        {
            if (_cam == null) _cam = Camera.main;
            var rt = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            var was = RenderTexture.active;
            try
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_cam, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt });
                RenderTexture.active = rt;
                var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                File.WriteAllBytes($"{Folder}/{name}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt); }
        }

        static void Place(Vector3 feet, Vector3 look)
        {
            var cc = _motor.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _motor.transform.position = feet + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            _motor.TurnTowards(look, 1f);
        }

        static string Member(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? (f.GetValue(o)?.ToString() ?? "null") : "?";
        }

        /// <summary>What the view centre is looking at, and what could make it shine.</summary>
        static void Surface(string place)
        {
            if (_cam == null) _cam = Camera.main;
            var ray = new Ray(_cam.transform.position, _cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, 80f, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy"), QueryTriggerInteraction.Ignore))
            { Note($"{place}: nothing ahead"); return; }
            var r = hit.collider.GetComponent<Renderer>() ?? hit.collider.GetComponentInParent<Renderer>();
            var sb = new StringBuilder($"{place}: {hit.collider.name} at {hit.distance:0.0} m");
            if (r != null)
                foreach (var m in r.sharedMaterials.Where(m => m != null).Take(5))
                    sb.Append($"\n    {m.name} smooth {(m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f):0.00} metal {(m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1f):0.00} bump {(m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : -1f):0.00}");
            var on = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.enabled && p.bounds.Contains(hit.point)).Select(p => $"{p.name} x{p.intensity:0.00}").ToArray();
            sb.Append($"\n    probes on at that point: {(on.Length == 0 ? "NONE (mirrors the sky)" : string.Join(", ", on))}; eye inside a probe: {Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Count(p => p.enabled && p.bounds.Contains(_cam.transform.position))}");
            Note(sb.ToString());
        }

        static Vector3 Ground(float x, float z, float y = 3f) =>
            NavMesh.SamplePosition(new Vector3(x, y, z), out var hit, 12f, NavMesh.AllAreas) ? hit.position : new Vector3(x, y, z);

        static System.Collections.IEnumerator Script()
        {
            _start = _motor.transform.position;
            yield return 3f;
            // ---- residents ------------------------------------------------------------------------------
            var town = Object.FindAnyObjectByType<TownHouseholds>(FindObjectsInactive.Include);
            if (town == null) Note("RESIDENTS: no TownHouseholds in the scene");
            else
            {
                Note($"RESIDENTS: component active {town.isActiveAndEnabled}, {town.houses.Length} houses, {town.shops.Length} shops, {town.spots.Length} spots, {town.prefabs?.Length} prefabs, player found {Member(town, "_player")}, max {Member(town, "_maxResidents")}, radius {Member(town, "_radius")}");
                int onMesh = 0, tested = 0;
                foreach (var s in town.spots) { tested++; if (NavMesh.SamplePosition(s.approach, out _, 1.2f, NavMesh.AllAreas)) onMesh++; }
                Note($"  spots on the navmesh within 1.2 m: {onMesh}/{tested}");
                int housesWith = town.houses.Count(h => Enumerable.Range(h.firstSpot, h.spotCount).Any(i => NavMesh.SamplePosition(town.spots[i].approach, out _, 1.2f, NavMesh.AllAreas)));
                Note($"  houses with at least one reachable-looking spot: {housesWith}/{town.houses.Length}");
                var house = town.houses.FirstOrDefault(h => Enumerable.Range(h.firstSpot, h.spotCount).Count(i => NavMesh.SamplePosition(town.spots[i].approach, out _, 1.2f, NavMesh.AllAreas)) >= 3);
                Place(house.door + Vector3.back * 0.3f, house.centre);
                Note($"  player at a door {house.door}, house centre {house.centre}, wants {house.residents} residents");
                yield return 6f;
                var lives = town.GetComponentsInChildren<CivilianLife>();
                Note($"  after 6 s: {lives.Length} residents and keepers alive under the town; {lives.Count(l => (l.transform.position - house.centre).magnitude < 15f)} within 15 m of this house");
                foreach (var l in lives.OrderBy(l => (l.transform.position - house.centre).sqrMagnitude).Take(8))
                {
                    var agent = l.GetComponent<NavMeshAgent>();
                    var an = l.GetComponent<Civilian>()?.animator;
                    Note($"    {l.name} at {l.transform.position} {(l.transform.position - house.centre).magnitude:0.0} m from centre; phase {Member(l, "_phase")}, spot {Member(l, "_spot")}, keeper {Member(l, "_keeper")}, agent on mesh {agent != null && agent.isOnNavMesh}, velocity {(agent != null ? agent.velocity.magnitude : 0f):0.00}, civ state {l.GetComponent<Civilian>()?.Current}, activity {(an != null ? an.GetInteger("Activity") : -1)}, renderers {l.GetComponentsInChildren<Renderer>().Count(x => x.enabled)}");
                }
                Note($"  house marks: {GameObject.Find("HouseRooms")?.transform.childCount} houses with rooms; with people {town.houses.Length}");
                int shots = 0;
                foreach (var l in lives.OrderBy(l => (l.transform.position - house.centre).sqrMagnitude).Take(10))
                {
                    if (l == null) continue;
                    Vector3 head = l.transform.position + Vector3.up * 1.2f;
                    float best = 0f; Vector3 from = l.transform.position;
                    for (int d = 0; d < 12; d++)
                    {
                        var dir = Quaternion.Euler(0f, d * 30f, 0f) * Vector3.forward;
                        float free = Physics.Raycast(head, dir, out var w, 3.2f, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy"), QueryTriggerInteraction.Ignore) ? w.distance : 3.2f;
                        if (free > best && NavMesh.SamplePosition(l.transform.position + dir * (free - 0.4f), out var nav, 0.8f, NavMesh.AllAreas)) { best = free; from = nav.position; }
                    }
                    Place(from, head);
                    yield return 1.2f;
                    var an = l.GetComponent<Civilian>()?.animator;
                    string activity = an != null ? ((LifeActivity)an.GetInteger("Activity")).ToString() : "?";
                    float floor = Physics.Raycast(l.transform.position + Vector3.up * 0.6f, Vector3.down, out var fh, 3f, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy"), QueryTriggerInteraction.Ignore) ? fh.point.y : float.NaN;
                    var hip = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
                    Note($"    seen {shots:00} {activity}: root {l.transform.position.y:0.00}, floor {floor:0.00}, hips {(hip != null ? hip.position.y : -1f):0.00}, stood {best:0.0} m off");
                    Shot($"resident_{shots++:00}_{activity}");
                }
            }

            // ---- gloss -----------------------------------------------------------------------------------
            foreach (var (name, at, look) in new[]
            {
                ("mosque_front", Ground(0f, 165f), new Vector3(0f, 8f, 190f)),
                ("mosque_court", Ground(0f, 218f), new Vector3(0f, 6f, 248f)),
                ("mosque_riwaq", Ground(-46f, 218f), new Vector3(-46f, 2f, 240f)),
                ("mosque_hall", Ground(0f, 270f), new Vector3(0f, 3f, 290f)),
                ("palace_hall", Ground(-385f, -11f), new Vector3(-385f, 2f, 1f)),
            })
            {
                Place(at, look);
                yield return 3f;
                Surface(name);
                Shot(name);
            }
            if (town != null && town.houses.Length > 0)
            {
                var h = town.houses[town.houses.Length / 2];
                Vector3 street = h.door + (h.door - h.centre).normalized * 8f;
                Place(Ground(street.x, street.z, h.door.y), h.centre + Vector3.up * 3f);
                yield return 3f;
                Surface("house_outside"); Shot("house_outside");
                Place(h.centre - Vector3.up * 0.3f, h.door + Vector3.up);
                yield return 3f;
                Surface("house_inside"); Shot("house_inside");
            }

            // ---- a drum ----------------------------------------------------------------------------------
            var drums = Object.FindObjectsByType<ExplosiveProp>(FindObjectsSortMode.None);
            Note($"DRUMS: {drums.Length} explosive props live");
            var drum = drums.FirstOrDefault(d => d.kind == ExplosiveProp.Kind.PetrolDrum && NavMesh.SamplePosition(d.transform.position, out _, 3f, NavMesh.AllAreas));
            if (drum != null)
            {
                Vector3 p = drum.transform.position;
                Place(Ground(p.x + 9f, p.z, p.y), p + Vector3.up * 0.5f);
                yield return 1f;
                Shot("drum_before");
                var neighbours = drums.Where(d => d != drum && (d.transform.position - p).magnitude < 8f).ToList();
                float hp = _health.Current; _heal = false;
                var dh = drum.GetComponent<Health>();
                Note($"  {drum.kind} at {p}, health {dh?.Current}, neighbours within 8 m {neighbours.Count}, player {(_motor.transform.position - p).magnitude:0.0} m away");
                for (int i = 0; i < 6 && dh != null && !dh.IsDead && drum.Current == ExplosiveProp.State.Intact; i++)
                {
                    dh.ApplyDamage(new DamageInfo(12f, p + Vector3.up * 0.5f, Vector3.right, Vector3.left, _motor.gameObject));
                    Note($"  hit {i + 1}: health {dh.Current:0}, state {drum.Current.ToString()}");
                    yield return 0.3f;
                }
                yield return 1f; Shot("drum_burning");
                Note($"  after the hits: state {drum.Current.ToString()}");
                yield return 7f;
                Shot("drum_after");
                Note($"  8 s on: drum {(drum == null ? "gone" : drum.Current.ToString())}; neighbours {string.Join(",", neighbours.Select(n => n == null ? "gone" : n.Current.ToString()))}; player health {hp:0} -> {_health.Current:0}");
                _heal = true; _health.ResetHealth();
            }

            // ---- a driver --------------------------------------------------------------------------------
            yield return 5f;
            var cars = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None);
            var drivers = cars.Select(c => c.GetComponentInChildren<VehicleDriver>()).Where(d => d != null).ToList();
            Note($"CARS: {cars.Length} traffic cars live, {drivers.Count} with a driver");
            var car = cars.FirstOrDefault(c => c.GetComponentInChildren<VehicleDriver>() != null);
            if (car != null)
            {
                car.Wait(40f);
                yield return 2f;
                var driver = car.GetComponentInChildren<VehicleDriver>();
                var b = new Bounds(car.transform.position, Vector3.zero);
                foreach (var r in car.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(r.bounds);
                var an = driver.GetComponentInChildren<Animator>();
                var head = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null;
                Note($"  car height {b.size.y:0.00} top {b.max.y:0.00}; driver head {(head != null ? head.position.y : -1f):0.00} (needs to be under the roof), hips {(an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips).position.y - car.transform.position.y : -1f):0.00} over the car's root; Activity {(an != null ? an.GetInteger("Activity") : -1)}, state hash known {(an != null && an.HasState(0, Animator.StringToHash("Life_Drive")))}");
                Vector3 front = car.transform.position + car.transform.forward * 5f;
                Place(Ground(front.x, front.z, car.transform.position.y), head != null ? head.position : car.transform.position + Vector3.up);
                yield return 1f;
                Shot("driver_front");
                Vector3 side = car.transform.position - car.transform.right * 3f + car.transform.forward * 0.1f;
                Place(Ground(side.x, side.z, car.transform.position.y), head != null ? head.position : car.transform.position + Vector3.up);
                yield return 1f;
                Shot("driver_side");
                if (head != null)
                {
                    Vector3 eye = _cam.transform.position;
                    var hits = Physics.RaycastAll(eye, (head.position - eye).normalized, 8f, ~LayerMask.GetMask("Player", "FirstPerson"), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3);
                    Note($"  a round through the side window meets: {string.Join(", ", hits.Select(h => h.collider.name + "(" + LayerMask.LayerToName(h.collider.gameObject.layer) + ")" + (h.collider.GetComponentInParent<VehicleDriver>() != null ? " DRIVER" : "")))}");
                    Vector3 fromFront = car.transform.position + car.transform.forward * 5f + Vector3.up * 1.5f;
                    var hitsF = Physics.RaycastAll(fromFront, (head.position - fromFront).normalized, 8f, ~LayerMask.GetMask("Player", "FirstPerson"), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3);
                    Note($"  a round through the windscreen meets: {string.Join(", ", hitsF.Select(h => h.collider.name + (h.collider.GetComponentInParent<VehicleDriver>() != null ? " DRIVER" : "")))}");
                }
                driver.Health?.ApplyDamage(new DamageInfo(500f, driver.transform.position, Vector3.up, Vector3.down, _motor.gameObject));
                yield return 2f;
                Note($"  driver killed: dead {driver.Dead}, car abandoned {car.Abandoned}, speed {car.Speed:0.0}");
                Shot("driver_dead");
            }

            // ---- a wall ------------------------------------------------------------------------------------
            Vector3 wallAt = Vector3.zero, wallNormal = Vector3.zero; bool found = false;
            for (int d = 0; d < 16 && !found; d++)
            {
                var dir = Quaternion.Euler(0f, d * 22.5f, 0f) * Vector3.forward;
                if (Physics.Raycast(_start + Vector3.up * 1.4f, dir, out var w, 40f, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy", "Vehicle"), QueryTriggerInteraction.Ignore) && Mathf.Abs(w.normal.y) < 0.2f && w.collider.GetComponentInParent<Door>() == null && !w.collider.name.Contains("Door"))
                { wallAt = w.point; wallNormal = w.normal; found = true; Note($"WALL: {w.collider.name}, surface {FPSImpactManager.SurfaceOf(w)}"); }
            }
            if (!found) Note("WALL: found no wall to punch");
            else
            {
                Vector3 stand = wallAt + wallNormal * 1.1f; stand.y = _start.y;
                Place(stand, wallAt);
                _heal = false; _health.ResetHealth();
                yield return 1f;
                Shot("wall_stand");
                for (int i = 0; i < 6; i++)
                {
                    while (Time.time < _melee.ReadyAt + 0.05f) yield return Step;
                    float hp = _health.Current;
                    _melee.TryPunch();
                    var thrown = _melee.Thrown;
                    if (i == 2) { yield return 0.15f; Shot("wall_punch"); }
                    yield return 0.6f;
                    var limbs = _motor.GetComponent<PlayerLimbs>();
                    Note($"WALL punch {i + 1} ({thrown}): left {limbs?.StateOf(PlayerLimbs.Limb.LeftHand)}, right {limbs?.StateOf(PlayerLimbs.Limb.RightHand)}; health {hp:0} -> {_health.Current:0}; surface {wallAt}");
                }
                _heal = true; _health.ResetHealth();
                _motor.TurnTowards(wallAt, 1f);
            }
            // ---- a fast car --------------------------------------------------------------------------------
            yield return 3f;
            TrafficCar fast = null; float waited = 0f;
            while (fast == null && waited < 30f)
            {
                fast = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).Where(c => !c.Abandoned && c.Speed > 6f).OrderByDescending(c => c.Speed).FirstOrDefault();
                if (fast == null) { waited += 1f; yield return 1f; }
            }
            if (fast == null) Note("RUN OVER: no car above 6 m/s in 30 s");
            else
            {
                float speed = fast.Speed;
                _heal = false; _health.SetMaxHealth(2000f); _health.ResetHealth();
                float before = _health.Current;
                Vector3 ahead = fast.transform.position + fast.transform.forward * Mathf.Max(5f, speed * 0.5f);
                Place(new Vector3(ahead.x, fast.transform.position.y, ahead.z), fast.transform.position + Vector3.up);
                float low = before; float closest = 99f;
                for (int i = 0; i < 90; i++)
                {
                    low = Mathf.Min(low, _health.Current);
                    if (fast != null) closest = Mathf.Min(closest, (fast.transform.position - _motor.transform.position).magnitude);
                    if (i == 15) Shot("car_coming");
                    yield return Step;
                }
                Note($"RUN OVER: car at {speed:0.0} m/s, stood {Mathf.Max(5f, speed * 0.5f):0.0} m ahead; closest {closest:0.0} m; player health {before:0} -> lowest {low:0}; car now {(fast != null ? fast.Speed : 0f):0.0} m/s");
                _heal = true; _health.ResetHealth();
            }

            Note("DONE");
        }

        /// <summary>Holds W down from the game's Update and reads the player's input in FixedUpdate, where the car reads it.</summary>
        class KeyHolder : MonoBehaviour
        {
            public UnityEngine.InputSystem.Keyboard keys;
            public PlayerMotor motor;
            public Vector2 read;
            public Vector2 readUpdate; public bool held;
            void Update()
            {
                readUpdate = motor.ReadMoveInput(); held = keys.wKey.isPressed;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keys, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            }
            void FixedUpdate() => read = motor.ReadMoveInput();
            void OnDestroy() => UnityEngine.InputSystem.InputSystem.QueueStateEvent(keys, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }

        static bool _worldRun;

        /// <summary>What his brief of 2026-11-05 asked for, in the real desert: glass breaking, a drum kicked, the horn, a driver running, a car's window, and how they look. Report and frames in Build/CloudCheck.</summary>
        public static void BatchWorld()
        {
            _worldRun = true;
            Batch();
        }

        static System.Collections.IEnumerator WorldScript()
        {
            Application.logMessageReceived += CountRed;
            yield return 8f;
            Note($"WORLD: player at {_motor.transform.position}");
            var field = GlassField.Instance;
            Note($"glass field: {(field != null ? field.panes.Length : 0)} panes, {(field != null ? field.chunks.Length : 0)} chunks");
            Vector3 me = _motor.transform.position;
            GlassField.Pane Nearest(GlassField.Kind kind)
            {
                GlassField.Pane best = default; float bestD = float.MaxValue;
                foreach (var p in field.panes)
                {
                    if (p.kind != kind || p.corner.y > 4f) continue;
                    float d = (p.corner - me).sqrMagnitude;
                    if (d < bestD && UnityEngine.AI.NavMesh.SamplePosition(p.corner + (p.u + p.v) * 0.5f + p.normal * 3f, out _, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) { bestD = d; best = p; }
                }
                return best;
            }
            foreach (var kind in new[] { GlassField.Kind.Display, GlassField.Kind.Window })
            {
                var pane = Nearest(kind);
                Vector3 mid = pane.corner + (pane.u + pane.v) * 0.5f;
                UnityEngine.AI.NavMesh.SamplePosition(mid + pane.normal * 3f, out var stand, 2.5f, UnityEngine.AI.NavMesh.AllAreas);
                Place(stand.position, mid);
                yield return 1f;
                Shot($"glass_{kind}_0_whole");
                Vector3 from = mid + pane.normal * 2f;
                var chunkMesh = field.chunks[pane.chunk].sharedMesh;
                var first = chunkMesh.vertices[pane.firstVertex];
                int rounds = 0;
                while (rounds < 4 && chunkMesh.vertices[pane.firstVertex + 1] != first)
                {
                    field.Ray(from, -pane.normal, 3f, GlassField.Blow.Round, _motor.gameObject);
                    rounds++;
                    yield return 0.2f;
                    Shot($"glass_{kind}_{rounds}");
                }
                Note($"{kind} pane at {mid}: broke on round {rounds}");
                yield return 1.2f;
                Shot($"glass_{kind}_settled");
            }

            // A drum, kicked.
            LooseProp drum = null; float near = float.MaxValue;
            foreach (var l in Object.FindObjectsByType<LooseProp>(FindObjectsSortMode.None))
            {
                float d = (l.transform.position - me).sqrMagnitude;
                if (l.GetComponent<ExplosiveProp>() == null && l.name.ToLowerInvariant().Contains("barrel") && d < near
                    && UnityEngine.AI.NavMesh.SamplePosition(l.transform.position, out var open, 2f, UnityEngine.AI.NavMesh.AllAreas) && Physics.Raycast(open.position + Vector3.up * 1.5f, Vector3.up, 30f, LayerMask.GetMask("Environment")) == false) { near = d; drum = l; }
            }
            Note($"loose props in the town: {Object.FindObjectsByType<LooseProp>(FindObjectsSortMode.None).Length}");
            if (drum != null)
            {
                Vector3 at = drum.transform.position;
                UnityEngine.AI.NavMeshHit s2 = default;
                foreach (var way in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    if (UnityEngine.AI.NavMesh.SamplePosition(at + way * 2.2f, out s2, 0.6f, UnityEngine.AI.NavMesh.AllAreas)
                        && !Physics.Linecast(s2.position + Vector3.up * 1.5f, at + Vector3.up * 0.6f, LayerMask.GetMask("Environment"), QueryTriggerInteraction.Ignore)) break;
                Place(s2.position, at + Vector3.up * 0.5f);
                yield return 0.5f;
                Shot("drum_before");
                var dir = (at - s2.position); dir.y = 0f;
                WorldHit.Send(drum.GetComponentInChildren<Collider>(), new WorldHitInfo { kind = WorldHitKind.Kick, point = at + Vector3.up * 0.5f, direction = dir.normalized, impulse = WorldHit.KickImpulse, source = _motor.gameObject });
                yield return 1.5f;
                Shot("drum_after");
                Note($"drum {drum.name} kicked: moved {(drum.transform.position - at).magnitude:0.00} m");
            }
            else Note("no inert drum found");

            var person = Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None).OrderBy(c => (c.transform.position - _motor.transform.position).sqrMagnitude).FirstOrDefault();
            if (person != null)
            {
                Place(person.transform.position + person.transform.forward * 3.5f, person.transform.position + Vector3.up * 1.2f);
                yield return 0.5f;
                Shot("person_close");
                var a = person.GetComponentInChildren<Animator>();
                Note($"person: {person.name}, height {person.GetComponentsInChildren<SkinnedMeshRenderer>().Max(r => r.bounds.max.y) - person.transform.position.y:0.00} m, triangles {person.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.triangles.Length) / 3}, human {a != null && a.isHuman}");
            }
            // The horn: stood on the road in front of a moving car.
            TrafficCar lead = null; float looked = 0f;
            while (lead == null && looked < 30f)
            {
                lead = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).OrderByDescending(c => c.Speed).FirstOrDefault(c => c.Speed > 4f && c.GetComponentInChildren<VehicleDriver>() != null);
                if (lead == null) { looked += 1f; yield return 1f; }
            }
            if (lead == null) { Note("NO moving car with a driver"); Finish(1); yield break; }
            Place(lead.transform.position + lead.transform.forward * 14f, lead.transform.position + Vector3.up);
            bool honked = false; float hornFor = 0f;
            for (float t = 0f; t < 8f && !honked; t += 0.1f)
            {
                var src = lead.GetComponent<AudioSource>();
                honked = src != null && src.isPlaying;
                hornFor = t;
                yield return 0.1f;
            }
            Shot("car_horn");
            Note($"horn at the player in the road: {honked} after {hornFor:0.0} s, car speed {lead.Speed:0.0}");

            // A car's window, from the side.
            var carGlass = lead.GetComponent<CarGlass>();
            int shownBefore = lead.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled);
            Vector3 side = lead.transform.TransformPoint(new Vector3(-6f, 1.05f, 0.1f));
            CarGlass.Ray(side, lead.transform.right, 8f, GlassField.Blow.Round);
            Note($"car window shot: {shownBefore} -> {lead.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled)} parts drawn, CarGlass {(carGlass != null ? "on" : "MISSING")}");
            yield return 0.3f;
            Shot("car_window");

            // Gunfire: the driver gets out and runs.
            int livingBefore = Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None).Length;
            NoiseSystem.Emit(_motor.transform.position, 90f, NoiseKind.Gunfire, _motor.gameObject);
            bool left = false; float leftAt = 0f;
            for (float t = 0f; t < 10f && !left; t += 0.1f)
            {
                left = lead.GetComponentInChildren<VehicleDriver>() == null;
                leftAt = t;
                if (!left) yield return 0.1f;
            }
            Note($"driver out of the car at gunfire: {left} after {leftAt:0.0} s; abandoned {lead.Abandoned}; civilians {livingBefore} -> {Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None).Length}");
            yield return 0.6f;
            var runner = Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None).OrderBy(c => (c.transform.position - lead.transform.position).sqrMagnitude).FirstOrDefault();
            if (runner != null) Note($"nearest civilian to the car: {(runner.transform.position - lead.transform.position).magnitude:0.0} m, fleeing {runner.Fleeing}, {runner.GetComponentInChildren<Animator>()?.runtimeAnimatorController?.name}");
            Shot("driver_running");
            yield return 2f;
            Shot("driver_running2");

            // How they look: someone close to, a bedroom, the pumps.
            var bed = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f => f.sharedMesh != null && f.sharedMesh.name.StartsWith("Bed_Double")).OrderBy(f => (f.transform.position - me).sqrMagnitude).FirstOrDefault();
            Note($"beds from Blender found: {Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Count(f => f.sharedMesh != null && f.sharedMesh.name.StartsWith("Bed_Double"))}");
            if (bed != null)
            {
                Place(bed.transform.position + bed.transform.parent.forward * 2f, bed.transform.position + Vector3.up * 0.5f);
                yield return 1f;
                Shot("bedroom");
            }
            var pump = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name.StartsWith("FuelPump"));
            if (pump != null)
            {
                Place(pump.transform.position + new Vector3(3.5f, 0f, -3.5f), pump.transform.position + Vector3.up);
                yield return 1f;
                Shot("gas_station");
            }
            Application.logMessageReceived -= CountRed;
            Note($"errors in the whole run: {_errors}");
            Note("DONE");
        }

        public static void BatchCar()
        {
            _carRun = true;
            Batch();
        }

        static bool _carRun;

        static void Set(DrivableCar car, string method, params object[] args) =>
            typeof(DrivableCar).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(car, args);

        /// <summary>The car driven as a player would: the view behind it, the steering's ramp, coasting, holding, a turn, a wall, getting out.</summary>
        static System.Collections.IEnumerator CarScript()
        {
            yield return 3f;
            var cars = Object.FindObjectsByType<DrivableCar>(FindObjectsSortMode.None);
            Note($"CAR: {cars.Length} drivable cars");
            var car = cars[0];
            var rb = car.GetComponent<Rigidbody>();
            var cam = Camera.main;
            // On a road, heading along it: behind a traffic car, wherever one is.
            TrafficCar lead = null; float looked = 0f;
            while (lead == null && looked < 20f)
            {
                lead = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).OrderByDescending(c => c.Speed).FirstOrDefault(c => c.Speed > 4f);
                if (lead == null) { looked += 1f; yield return 1f; }
            }
            if (lead != null)
            {
                Vector3 onRoad = lead.transform.position - lead.transform.forward * 30f + Vector3.up * 0.4f;
                car.transform.SetPositionAndRotation(onRoad, lead.transform.rotation);
                Physics.SyncTransforms();
                Note($"  put on a road at {onRoad}");
            }
            Vector3 spot = car.transform.TransformPoint(car.exitOffset);
            Place(spot, car.transform.position);
            yield return 0.5f;
            Set(car, "GetIn");
            Note($"  got in: driving {DrivableCar.Driving == car}, kinematic {rb.isKinematic}, camera parent {(cam.transform.parent != null ? cam.transform.parent.name : "none")}");
            yield return 1f;
            Shot("car_seated");
            float y0 = car.transform.position.y;
            Note($"  sitting 1 s: camera {(cam.transform.position - car.transform.position).magnitude:0.0} m from the car (origin bug: {(cam.transform.position.magnitude < 1f ? "YES" : "no")}), speed {rb.linearVelocity.magnitude:0.00}");
            // The keyboard, as the player drives it: the keys are separate buttons on the Move action, and reading that as a Vector2 threw on every
            // held key in FixedUpdate, so the car took no input and the console went red. The touch path above never went that way.
            var keys = UnityEngine.InputSystem.Keyboard.current ?? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            MobileInput.Move = Vector2.zero;
            MobileInput.Active = false;
            yield return 2f;
            int redBefore = _errors;
            Application.logMessageReceived += CountRed;
            float before = rb.linearVelocity.magnitude;
            // A batch editor has no focus, and by default drops a device's input without it.
            var inputSettings = UnityEngine.InputSystem.InputSystem.settings;
            var wasBackground = inputSettings.backgroundBehavior; var wasEditor = inputSettings.editorInputBehaviorInPlayMode;
            inputSettings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            inputSettings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // NOTE: a batch editor never delivers these keys to the game (W reads False in Update, 2026-11-05), so this cannot prove the keyboard;
            // it does prove that nothing throws on this path. Real keys: a play-test.
            var holder = new GameObject("KeyHolder").AddComponent<KeyHolder>();
            holder.keys = keys; holder.motor = _motor;
            for (int held = 0; held < 4; held++)
            {
                yield return 1f;
                Note($"    {held + 1} s: read in Update {holder.readUpdate} (W {holder.held}), in FixedUpdate {holder.read}, rear torque {car.rearLeft.motorTorque:0} brake {car.rearLeft.brakeTorque:0}, speed {rb.linearVelocity.magnitude:0.0} m/s, moved {(car.transform.position - spot).magnitude:0.0} m");
            }
            Object.Destroy(holder.gameObject);
            inputSettings.backgroundBehavior = wasBackground; inputSettings.editorInputBehaviorInPlayMode = wasEditor;
            Note($"  keyboard W for 4 s: {before:0.0} -> {rb.linearVelocity.magnitude:0.0} m/s, errors {_errors - redBefore}");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keys, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();
            Application.logMessageReceived -= CountRed;
            MobileInput.Active = true;
            yield return 1f;
            // Steering ramp, from a key's full left.
            MobileInput.Move = new Vector2(0f, 1f);
            float top = 0f;
            for (int i = 0; i < 240; i++)
            {
                top = Mathf.Max(top, rb.linearVelocity.magnitude);
                if (i % 30 == 29) Note($"  throttle {(i + 1) * Step:0.0} s: {rb.linearVelocity.magnitude:0.0} m/s, camera {(cam.transform.position - car.transform.position).magnitude:0.0} m behind, on the wheels {car.frontLeft.isGrounded && car.rearRight.isGrounded}, moved {(car.transform.position - spot).magnitude:0} m");
                yield return Step;
            }
            Shot("car_moving");
            yield return 0.05f;
            MobileInput.Move = new Vector2(-1f, 1f);
            float a0 = car.frontLeft.steerAngle; yield return Step; yield return Step;
            Note($"  steer key: 2 frames gives {car.frontLeft.steerAngle - a0:0.0} degrees (was full lock at once)");
            float yaw0 = car.transform.eulerAngles.y;
            yield return 1.5f;
            Note($"  turning 1.5 s: heading changed {Mathf.DeltaAngle(yaw0, car.transform.eulerAngles.y):0} degrees, roll {Mathf.DeltaAngle(0f, car.transform.eulerAngles.z):0}, pitch {Mathf.DeltaAngle(0f, car.transform.eulerAngles.x):0}, speed {rb.linearVelocity.magnitude:0.0}");
            Shot("car_turning");
            MobileInput.Move = Vector2.zero;
            float coast0 = rb.linearVelocity.magnitude;
            yield return 2f;
            Note($"  no pedals 2 s: {coast0:0.0} -> {rb.linearVelocity.magnitude:0.0} m/s");
            MobileInput.Move = new Vector2(0f, -1f);
            float brake0 = rb.linearVelocity.magnitude; float t = 0f;
            while (rb.linearVelocity.magnitude > 1f && t < 8f) { t += Step; yield return Step; }
            Note($"  full brake from {brake0:0.0} m/s: stopped in {t:0.0} s");
            yield return 1f;
            MobileInput.Move = new Vector2(0f, -1f);
            yield return 3f;
            Note($"  reverse 3 s: {Vector3.Dot(rb.linearVelocity, car.transform.forward):0.0} m/s (limit {car.reverseSpeed})");
            MobileInput.Move = Vector2.zero;
            yield return 3f;
            Vector3 still = car.transform.position;
            yield return 2f;
            Note($"  no pedals, stopped: drifted {(car.transform.position - still).magnitude:0.00} m in 2 s");
            // A wall behind the chase camera.
            for (int d = 0; d < 16; d++)
            {
                var dir = Quaternion.Euler(0f, d * 22.5f, 0f) * Vector3.forward;
                if (!Physics.Raycast(car.transform.position + Vector3.up, dir, out var w, 60f, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy", "Vehicle"), QueryTriggerInteraction.Ignore) || Mathf.Abs(w.normal.y) > 0.2f || w.distance < 4f) continue;
                car.transform.SetPositionAndRotation(new Vector3(w.point.x, car.transform.position.y + 0.3f, w.point.z) + w.normal * 3.6f, Quaternion.LookRotation(w.normal));
                Physics.SyncTransforms();
                yield return 1f;
                Vector3 pivot = car.transform.position + Vector3.up * car.cameraPivot;
                Note($"  back to a wall {w.collider.name}, 3.6 m behind: camera {(cam.transform.position - car.transform.position).magnitude:0.0} m from the car, wall between it and the car {Physics.Linecast(pivot, cam.transform.position, ~LayerMask.GetMask("Player", "FirstPerson", "Civilian", "Enemy", "Vehicle"), QueryTriggerInteraction.Ignore)}");
                Shot("car_wall");
                break;
            }
            Set(car, "GetOut", false);
            yield return 0.5f;
            var motor = _motor;
            bool clear = !Physics.CheckCapsule(motor.transform.position + Vector3.up * 0.5f, motor.transform.position + Vector3.up * 1.2f, 0.3f, ~LayerMask.GetMask("Player", "PlayerBounds", "FirstPerson", "Ignore Raycast", "UI"), QueryTriggerInteraction.Ignore);
            Note($"  got out: driving {DrivableCar.Driving != null}, {(motor.transform.position - car.transform.position).magnitude:0.0} m from the car, standing clear {clear}, camera parent {(cam.transform.parent != null ? cam.transform.parent.name : "none")}, local {cam.transform.localPosition}, mask restored {(cam.cullingMask & LayerMask.GetMask("Player")) != 0}");
            yield return 1f;
            Shot("car_out");
            Note($"  top speed reached in 5 s: {top:0.0} m/s");
            Note("DONE");
        }

        /// <summary>A mission level: who is where, and the boss or the target.</summary>
        static System.Collections.IEnumerator LevelScript()
        {
            yield return 12f;
            var lm = Object.FindAnyObjectByType<LevelManager>();
            Note($"LEVEL {lm.LevelNumber}: {lm.Level.objective}; boss {lm.HasBoss} '{lm.ActiveBossName}', bossPlace '{lm.Level.bossPlace}', target '{lm.Level.targetName}' at '{lm.Level.targetPlace}', guards {lm.Level.targetGuards}/{lm.Level.bossGuards}, bossScale {lm.Level.bossScale}");
            Note($"  player at {_motor.transform.position}");
            var ais = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            foreach (var g in ais.GroupBy(a => (a.archetype != null ? a.archetype.name : "?") + "/" + (a.archetype != null ? a.archetype.role.ToString() : "?")))
                Note($"  {g.Count()} {g.Key}, scale {g.First().transform.lossyScale.y:0.00}, nearest the player {g.Min(a => (a.transform.position - _motor.transform.position).magnitude):0} m");
            var boss = lm.ActiveBoss != null ? lm.ActiveBoss.GetComponent<EnemyAI>() : ais.FirstOrDefault(a => a.archetype != null && a.archetype.role == EnemyArchetype.Role.Boss);
            if (boss == null) boss = ais.OrderByDescending(a => a.GetComponent<Health>().maxHealth).FirstOrDefault();
            if (boss != null)
            {
                Vector3 first = boss.transform.position;
                Note($"  the chief {boss.archetype?.name} at {first}, state {boss.CurrentState}; guards within 15 m {ais.Count(a => a != boss && (a.transform.position - first).magnitude < 15f)}");
                for (int t = 10; t <= 60; t += 10)
                {
                    yield return 10f;
                    Note($"  {t} s on: {(boss.transform.position - first).magnitude:0.0} m from his post, {(boss.transform.position - _motor.transform.position).magnitude:0} m from the player, state {boss.CurrentState}, enemies heading off {ais.Count(a => a != null && (a.transform.position - first).magnitude > 25f)}");
                }
                Vector3 at = boss.transform.position - boss.transform.forward * -6f;
                Place(Ground(at.x, at.z, at.y), boss.transform.position + Vector3.up * 1.5f);
                yield return 1.5f;
                Shot($"level{lm.LevelNumber}_chief");
            }
            Note("DONE");
        }
    }
}
#endif
