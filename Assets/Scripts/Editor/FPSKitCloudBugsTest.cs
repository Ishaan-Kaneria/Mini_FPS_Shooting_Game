#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The combat and vehicle bugs from the cloud session's list (Docs/DESERT_BUGS_AND_PERFORMANCE.md), in the small AI test scene so no
    /// arena has to load: D1 a neck round on a weakened man, D2 fire rate at any frame rate (and tapping cannot beat it), D3 blasts and
    /// walls, C1 whose death it is, B9 a car rolling on through a man, B10 rounds through a car's glass, O2 a car with a dead driver.
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitCloudBugsTest.Batch
    /// </summary>
    public static class FPSKitCloudBugsTest
    {
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int _step, _lastFrame = -1;
        static float _t0, _struckAt = -1f, _struckZ;
        static double _real;
        static GameObject _player, _man;
        static DrivableCar _car;
        static Rigidbody _body;
        static TrafficCar _traffic;
        static Vector3 _trafficFrom;
        static readonly StringBuilder _log = new StringBuilder();
        static readonly List<string> _fails = new List<string>();

        public static void Batch()
        {
            FPSKitAITestScene.Build();
            FPSKitPlayMode.SuspendStartScene();
            SaveMigration.Apply();
            _real = EditorApplication.timeSinceStartup;
            _step = 0;
            _struckAt = -1f;
            _kickChecked = false; _kicked = null;
            EditorApplication.update += Tick;
        }

        static void Require(bool ok, string what) { _log.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) _fails.Add(what); }

        static GameObject Enemy(string name, Vector3 at, bool staged)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), at, Quaternion.LookRotation(Vector3.back));
            go.name = name;
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(go);
            if (staged)
            {
                if (go.TryGetComponent<EnemyBrain>(out var brain)) Object.DestroyImmediate(brain);
                if (go.TryGetComponent<EnemyAI>(out var ai)) ai.enabled = false;
            }
            return go;
        }

        static Health Target(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = at;
            go.SetActive(false);
            var h = go.AddComponent<Health>();
            h.maxHealth = 1000f;
            go.SetActive(true);
            return h;
        }

        static void Wall(Vector3 at, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = at + Vector3.up * size.y * 0.5f;
            go.transform.localScale = size;
            go.layer = LayerMask.NameToLayer("Environment");
        }

        static int RoundsIn(float seconds, float fps, float interval)
        {
            float due = 0f, frame = 1f / fps;
            int rounds = 0;
            for (float t = 0f; t < seconds; t += frame)
                if (t >= due) { due = Weapon.NextShotTime(t, due, interval, frame); rounds++; }
            return rounds;
        }

        static void CheckNeck()
        {
            var man = Enemy("WeakNeck", new Vector3(0f, 0f, 8f), false);
            var hp = man.GetComponent<Health>();
            hp.ApplyDamage(hp.maxHealth * 0.9f);
            float before = hp.Current;
            foreach (var hb in man.GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != BodyPart.Neck || !hb.isActiveAndEnabled) continue;
                Vector3 p = hb.transform.position;
                hb.Receive(new DamageInfo(1f, p, Vector3.back, Vector3.forward, _player));
                break;
            }
            Require(hp.Current < before, $"a light round in the neck hurts a man already below the wound's floor ({before:0.0} -> {hp.Current:0.0})");
        }

        static void CheckFireRate()
        {
            const float interval = 60f / 900f;
            int at30 = RoundsIn(10f, 30f, interval), at60 = RoundsIn(10f, 60f, interval), at144 = RoundsIn(10f, 144f, interval);
            Require(Mathf.Abs(at30 - 150) <= 4 && Mathf.Abs(at60 - 150) <= 4 && Mathf.Abs(at144 - 150) <= 4, $"900 RPM fires its 150 rounds in 10 s at 30, 60 and 144 fps ({at30}, {at60}, {at144})");
            float afterTap = Weapon.NextShotTime(10.2f, 10f, 0.25f, 1f / 60f);
            Require(Mathf.Approximately(afterTap, 10.45f), $"a fresh press counts from now, so tapping cannot beat the gun ({afterTap:0.00})");
        }

        static void CheckBlast()
        {
            Vector3 c = new Vector3(30f, 0.2f, -30f);
            var open = Target(c + new Vector3(3f, 0.8f, 0f));
            var walled = Target(c + new Vector3(-3f, 0.8f, 0f));
            var crated = Target(c + new Vector3(0f, 0.8f, 3f));
            Wall(new Vector3(28.5f, 0f, -30f), new Vector3(0.3f, 4f, 3f));
            Wall(new Vector3(30f, 0f, -28.5f), new Vector3(2f, 0.8f, 0.3f));
            Physics.SyncTransforms();
            var spec = new BlastSpec { damage = 100f, radius = 8f, edgeDamageFraction = 1f, falloffPower = 1f, selfDamageFraction = 0f, explosionForce = 0f };
            Explosion.Blast(c, spec, null, ~0);
            float hitOpen = 1000f - open.Current, hitWalled = 1000f - walled.Current, hitCrated = 1000f - crated.Current;
            Require(hitOpen > 0f && hitWalled < hitOpen * 0.2f && hitWalled > 0f, $"a wall takes most of a blast ({hitOpen:0} in the open, {hitWalled:0} behind it)");
            Require(Mathf.Approximately(hitCrated, hitOpen), $"a waist-high crate does not shelter a man's head ({hitCrated:0})");
        }

        static void CheckCredit()
        {
            var enemy = new GameObject("Enemy");
            Require(CivilianCasualties.ByPlayer(_player), "a death by the player is his");
            Require(!CivilianCasualties.ByPlayer(enemy) && !CivilianCasualties.ByPlayer(null), "a death by an enemy or a charge is not");
            Object.Destroy(enemy);
        }

        static void CheckGlass()
        {
            Vector3 a = new Vector3(-10f, 1f, -30f), u = new Vector3(2f, 0f, 0f), v = new Vector3(0f, 1.5f, 0f);
            var go = new GameObject("Glass_test");
            var mesh = new Mesh();
            mesh.SetVertices(new[] { a, a + u, a + u + v, a + v });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GlassFX.Pane;
            var holder = new GameObject("GlassField");
            holder.SetActive(false);
            var field = holder.AddComponent<GlassField>();
            field.chunks = new[] { mf };
            field.panes = new[] { new GlassField.Pane { corner = a, u = u, v = v, normal = Vector3.back, kind = GlassField.Kind.Window, chunk = 0, firstVertex = 0 } };
            holder.SetActive(true);
            Require(GlassField.Instance == field, "the field is found");
            Vector3 from = a + (u + v) * 0.5f + Vector3.back * 5f;
            Require(field.Ray(from + Vector3.right * 30f, Vector3.forward, 20f, GlassField.Blow.Round, null) == 0, "a shot past a pane does nothing");
            Require(field.Ray(from, Vector3.forward, 20f, GlassField.Blow.Round, null) == 1, "a round on the line strikes it");
            Require(mesh.vertices[1] != mesh.vertices[0], "the first round only cracks ordinary glass");
            field.Ray(from, Vector3.forward, 20f, GlassField.Blow.Round, null);
            Require(mesh.vertices[1] == mesh.vertices[0], "the second breaks it: its quad is gone");
            Require(field.Ray(from, Vector3.forward, 20f, GlassField.Blow.Heavy, null) == 0, "broken stays broken: the round passes");
            var shard = GameObject.Find("Shards");
            Require(shard != null, "shards fall");
            Require(WorldSounds.GlassShatter(false) != null && WorldSounds.GlassCrack() != null && WorldSounds.Horn() != null, "its sounds are there");
            Require(GlassFX.Pane != null && GlassFX.Pane.GetFloat("_Metallic") == 0f, "glass is no metal");
        }

        static Transform _kicked;
        static Vector3 _kickedFrom;
        static bool _kickChecked;

        static void CheckLoose()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FPSKitTownAssets.PropFolder + "/Barrel_01.prefab");
            if (prefab == null) { Require(false, "Barrel_01 prefab"); return; }
            var drum = Object.Instantiate(prefab, new Vector3(10f, 0.02f, -32f), Quaternion.identity);
            Require(drum.GetComponent<LooseProp>() != null, "a drum is loose");
            Require(drum.GetComponent<Rigidbody>().isKinematic, "and costs nothing until it is struck");
            Require(drum.GetComponentInChildren<MeshRenderer>() != null && drum.GetComponentInChildren<MeshRenderer>().sharedMaterial != null && drum.GetComponentInChildren<MeshRenderer>().sharedMaterial.name.StartsWith("Drum"), "it is the model from Blender");
            _kicked = drum.transform; _kickedFrom = drum.transform.position;
            var col = drum.GetComponentInChildren<Collider>();
            WorldHit.Send(col, new WorldHitInfo { kind = WorldHitKind.Kick, point = drum.transform.position + Vector3.up * 0.5f + Vector3.back * 0.3f, direction = Vector3.forward, impulse = WorldHit.KickImpulse });
            Require(!drum.GetComponent<Rigidbody>().isKinematic, "a kick frees it");
        }

        static void MakeCar()
        {
            var holder = new GameObject("TestCar");
            holder.transform.position = new Vector3(-30f, 0.1f, -30f);
            holder.layer = LayerMask.NameToLayer("Vehicle");
            var box = holder.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.715f, 0f);
            box.size = new Vector3(1.8f, 0.91f, 4.4f);
            _body = holder.AddComponent<Rigidbody>();
            _car = holder.AddComponent<DrivableCar>();
            holder.AddComponent<VehicleStrike>();
            var wheels = new WheelCollider[4];
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Wheel" + i);
                w.transform.SetParent(holder.transform, false);
                w.transform.localPosition = new Vector3(i % 2 == 0 ? -0.8f : 0.8f, 0.4f, i < 2 ? 1.4f : -1.4f);
                wheels[i] = w.AddComponent<WheelCollider>();
                wheels[i].radius = 0.34f;
                wheels[i].mass = 22f;
                wheels[i].suspensionDistance = 0.22f;
                var spring = wheels[i].suspensionSpring; spring.spring = 32000f; spring.damper = 4200f; spring.targetPosition = 0.45f; wheels[i].suspensionSpring = spring;
            }
            _car.frontLeft = wheels[0]; _car.frontRight = wheels[1]; _car.rearLeft = wheels[2]; _car.rearRight = wheels[3];
            _car.wheelVisuals = new Transform[4];
            // No CharacterController here to switch off: the stand-in's capsule would shove the car it sits in.
            if (_player.TryGetComponent<Collider>(out var own)) Object.Destroy(own);
            typeof(DrivableCar).GetMethod("GetIn", Any).Invoke(_car, null);
            _man = Enemy("InTheRoad", new Vector3(-30f, 0f, -20f), true);
            // Tough enough to take the blow on his feet, as an elite or a boss does: knocked down, his loose limbs would part for any car.
            var tough = _man.GetComponent<Health>();
            tough.maxHealth = 100000f;
            tough.ResetHealth();
        }

        static void CheckRounds()
        {
            var health = _player.GetComponent<Health>();
            Require(DrivableCar.SeatOf(_player.transform, out var seat) && seat.y < _car.transform.position.y + 1.17f, $"enemies aim into the cabin, not over the roof (seat {seat.y - _car.transform.position.y:0.00} above the car)");
            Require(!DrivableCar.SeatOf(_man.transform, out _), "only the driver is aimed at in his seat");

            bool Fire(float height, out float taken)
            {
                float before = health.Current;
                Vector3 from = _car.transform.TransformPoint(new Vector3(-6f, height, 0f));
                bool took = Physics.Raycast(from, _car.transform.right, out var hit, 8f, 1 << _car.gameObject.layer, QueryTriggerInteraction.Ignore)
                            && DrivableCar.TakeRound(hit, new DamageInfo(25f, hit.point, hit.normal, _car.transform.right, _man));
                taken = before - health.Current;
                return took;
            }
            Require(Fire(0.95f, out float glass) && glass > 0f, $"a round through the side glass reaches the driver ({glass:0})");
            Require(!Fire(0.45f, out float door) && door == 0f, "a round into the door stops in the car's body");
            typeof(DrivableCar).GetMethod("GetOut", Any).Invoke(_car, new object[] { true });
        }

        static void MakeTraffic()
        {
            Wall(new Vector3(30f, 0f, 14f), new Vector3(6f, 3f, 0.5f));
            Physics.SyncTransforms();
            var directorHost = new GameObject("TestDirector");
            var director = directorHost.AddComponent<TrafficDirector>();
            director.enabled = false;     // no spawning: only here to say the car is driven
            var go = new GameObject("TestTraffic");
            go.transform.position = _trafficFrom = new Vector3(30f, 0f, 0f);
            _traffic = go.AddComponent<TrafficCar>();
            _traffic.halfLength = 2.2f; _traffic.halfWidth = 0.9f;
            typeof(TrafficCar).GetField("director", Any).SetValue(_traffic, director);
            typeof(TrafficCar).GetField("_driverDead", Any).SetValue(_traffic, true);
            typeof(TrafficCar).GetField("_speed", Any).SetValue(_traffic, 8f);
        }

        static void Finish()
        {
            Debug.Log("[FPSKitBatch] cloud bugs:\n" + _log);
            Debug.Log(_fails.Count == 0 ? "[FPSKitBatch] PASSED: FPSKitCloudBugsTest" : $"[FPSKitBatch] FAILED: FPSKitCloudBugsTest: {string.Join("; ", _fails)}");
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            EditorApplication.ExitPlaymode();
            FPSKitPlayMode.RestoreStartScene();
            FPSKitBatch.Exit(_fails.Count == 0 ? 0 : 1);
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _real > 300) throw new Exception("cloud bugs test timed out");
                if (!EditorApplication.isPlaying) { if (_step == 0) { EditorApplication.EnterPlaymode(); _step = 1; } return; }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 60f;
                switch (_step)
                {
                    case 1:
                        if (Time.frameCount < 10) return;
                        _player = GameObject.FindGameObjectWithTag("Player");
                        CheckNeck();
                        CheckFireRate();
                        CheckBlast();
                        CheckCredit();
                        CheckGlass();
                        CheckLoose();
                        MakeCar();
                        _step = 2; _t0 = Time.time;
                        return;
                    case 2:
                        // Held at town speed until the bumper's blow lands: from then on it is on its own, to see what he does to it.
                        var hp = _man.GetComponent<Health>();
                        if (_struckAt < 0f)
                        {
                            _body.linearVelocity = Vector3.forward * 9f;
                            if (hp.Current < hp.maxHealth) { _struckAt = Time.time; _struckZ = _car.transform.position.z; }
                            else if (Time.time - _t0 > 6f) { Require(false, "the bumper's blow lands"); Finish(); }
                            return;
                        }
                        if (Time.time - _struckAt < 0.5f) return;
                        float speed = _body.linearVelocity.magnitude, on = _car.transform.position.z - _struckZ;
                        Require(on > 2.5f && speed > 4f, $"a car at town speed rolls on through a man it strikes ({on:0.0} m further, {speed:0.0} m/s after 0.5 s)");
                        Require(hp.Current < hp.maxHealth, $"and he is hurt ({hp.Current:0} of {hp.maxHealth:0})");
                        CheckRounds();
                        MakeTraffic();
                        _step = 3; _t0 = Time.time;
                        return;
                    case 3:
                        if (_kicked != null && !_kickChecked && Time.time - _t0 > 1f)
                        {
                            _kickChecked = true;
                            Require((_kicked.position - _kickedFrom).magnitude > 0.3f, $"a kicked drum moves ({(_kicked.position - _kickedFrom).magnitude:0.00} m in a second)");
                        }
                        if (Time.time - _t0 < 6f) return;
                        Require(_traffic != null, "a car with a dead driver is not removed");
                        if (_traffic != null)
                        {
                            Vector3 p = _traffic.transform.position;
                            Require(_traffic.Speed == 0f, $"it comes to rest ({_traffic.Speed:0.0} m/s)");
                            Require(Mathf.Abs(p.x - _trafficFrom.x) < 0.01f, "it rolls straight, taking no corners");
                            Require(p.z > _trafficFrom.z + 5f && p.z + _traffic.halfLength < 14f, $"and stops at the wall in its way (front at {p.z + _traffic.halfLength:0.0}, wall at 14)");
                        }
                        Require(!_kicked.GetComponent<Rigidbody>().IsSleeping() || _kicked.GetComponent<Rigidbody>().isKinematic, "and comes to rest without staying awake");
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[FPSKitBatch] FAILED: FPSKitCloudBugsTest: " + e);
                EditorApplication.update -= Tick;
                Time.captureDeltaTime = 0f;
                FPSKitBatch.Exit(1);
            }
        }
    }
}
#endif
