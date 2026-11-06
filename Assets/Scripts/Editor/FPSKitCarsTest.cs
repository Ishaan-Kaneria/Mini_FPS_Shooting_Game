#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The cars and glass in the real desert, with pictures and numbers (Build/CarsTest): each kind of car, a windscreen starred, a side window falling out, a house window cracking and coming apart, the driver's seat,
    /// the gun up with the car still moving, and the handling measured. <c>UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitCarsTest.Batch</c>. Keys cannot be simulated in batch: driving goes through the touch input, which is the same
    /// ReadMoveInput the keys go through.
    /// </summary>
    public static class FPSKitCarsTest
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";
        const string Folder = "Build/CarsTest";
        const float Step = 1f / 60f;
        static readonly StringBuilder Log = new StringBuilder();
        static IEnumerator _script;
        static PlayerMotor _motor;
        static int _lastFrame = -1, _wait, _failures, _reds;
        static float _deadline;
        static Camera _shotCam;
        static string _only;

        public static void Batch()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-fpskitOnly");
            _only = i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
            Directory.CreateDirectory(Folder);
            Log.Clear();
            EditorSceneManager.OpenScene(Scene);
            FPSKitPlayMode.SuspendStartScene();
            _script = null; _motor = null;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Note(string s) { Log.Append(s).Append('\n'); Debug.Log("[CarsTest] " + s); }
        static void Require(bool ok, string what) { Note((ok ? "ok   " : "FAIL ") + what); if (!ok) _failures++; }
        static void Red(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) { _reds++; Note("RED: " + message); } }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (_motor == null)
                {
                    _motor = Object.FindAnyObjectByType<PlayerMotor>();
                    if (_motor == null) return;
                    Time.captureDeltaTime = Step;
                    MobileInput.Active = true;
                    Application.logMessageReceived += Red;
                    _script = Script();
                    _deadline = Time.realtimeSinceStartup + 900f;
                }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                if (_motor.TryGetComponent<Health>(out var h) && h.Normalized < 0.9f) h.ResetHealth();
                if (Time.realtimeSinceStartup > _deadline) { Note("TIMED OUT"); Finish(1); return; }
                if (_wait > 0) { _wait--; return; }
                if (!_script.MoveNext()) Finish(_failures + _reds > 0 ? 1 : 0);
                else if (_script.Current is float s) _wait = Mathf.RoundToInt(s / Step);
            }
            catch (System.Exception e) { Note("EXCEPTION " + e); Finish(1); }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Red;
            Note($"failures {_failures}, red errors {_reds}");
            File.WriteAllText($"{Folder}/report.txt", Log.ToString());
            Time.captureDeltaTime = 0f;
            MobileInput.Active = false;
            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.Exit(code);
        }

        // ---- pictures ---------------------------------------------------------------
        static void Save(Camera cam, string name, int w = 960, int h = 540)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var was = RenderTexture.active;
            try
            {
                DynamicGI.UpdateEnvironment();
                var req = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, req);
                RenderPipeline.SubmitRenderRequest(cam, req);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"{Folder}/{name}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>A picture from anywhere: a camera of its own, as the world's main one is set up.</summary>
        static void From(Vector3 at, Vector3 look, string name, float fov = 45f)
        {
            if (_shotCam == null)
            {
                var go = new GameObject("TestShotCam");
                _shotCam = go.AddComponent<Camera>();
                _shotCam.enabled = false;
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_shotCam);
                data.renderPostProcessing = true;
                _shotCam.cullingMask = ~LayerMask.GetMask("FirstPerson", "Player");
                _shotCam.nearClipPlane = 0.05f;
            }
            _shotCam.fieldOfView = fov;
            _shotCam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look - at));
            Save(_shotCam, name);
        }

        static MethodInfo Private(System.Type t, string name) => t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

        // ---- the script ---------------------------------------------------------------
        static IEnumerator Script()
        {
            yield return 6f;
            Note("level running; player at " + _motor.transform.position);

            // Which parked cars there are.
            var parked = Object.FindObjectsByType<CarGlass>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(g => g.transform.parent != null && g.transform.parent.name.StartsWith("Cars_")).ToArray();
            var byKind = parked.GroupBy(g => g.transform.parent.name.Substring(5)).ToDictionary(g => g.Key, g => g.ToArray());
            Note("parked: " + string.Join(", ", byKind.Select(k => $"{k.Key} {k.Value.Length}")));
            Require(byKind.Count >= 6, "at least six kinds parked");
            var colours = parked.Select(g => g.GetComponentInChildren<MeshRenderer>().sharedMaterials[0].name).Distinct().ToArray();
            Note("colours: " + string.Join(", ", colours));
            Require(colours.Length >= 10, "at least ten colours parked");
            var traffic = Object.FindAnyObjectByType<TrafficDirector>();
            Require(traffic != null && traffic.carPrefabs.Length >= 6 && traffic.paints.Length >= 10, "traffic has the new cars and colours");

            if (_only == null || _only.Contains("kinds"))
            {
                foreach (var kv in byKind)
                {
                    var car = kv.Value[0].transform;
                    // From a side with nothing in the way.
                    Vector3 eye = car.position + Vector3.up * 1.5f + car.forward * 6f;
                    foreach (var dir in new[] { car.forward + car.right, car.forward - car.right, -car.forward + car.right, -car.forward - car.right, car.forward, -car.forward })
                    {
                        Vector3 e = car.position + dir.normalized * 6f + Vector3.up * 1.5f;
                        if (Physics.CheckSphere(e, 0.4f) || Physics.Linecast(e, car.position + Vector3.up * 1.6f + dir.normalized * 2.4f)) continue;
                        eye = e; break;
                    }
                    From(eye, car.position + Vector3.up * 0.8f, "kind_" + kv.Key);
                    yield return 0.05f;
                }
                var first = parked[0].transform;
                From(first.position + first.forward * 18f + Vector3.up * 2f, first.position + Vector3.up, "street");
                yield return 0.05f;
            }

            if (_only == null || _only.Contains("glass"))
            {
                // A parked car's glass: windscreen stars, side window goes.
                var car = byKind["Saloon"][0];
                var t = car.transform;
                Vector3 front = t.position + t.forward * 4.5f + Vector3.up * 1.25f;
                Vector3 aim = t.position + t.forward * 0.45f + Vector3.up * 1.15f + t.right * 0.2f;
                From(front, aim, "wind_0_whole", 28f);
                GlassBreak.Ray(front, (aim - front).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                yield return 0.1f;
                From(front, aim, "wind_1_one_round", 28f);
                Vector3 close = t.position + t.forward * 2.6f + t.right * 0.4f + Vector3.up * 1.45f;
                From(close, aim, "wind_1_close", 32f);
                Vector3 aim2 = aim + t.right * -0.45f + Vector3.up * 0.04f;
                GlassBreak.Ray(front, (aim2 - front).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                yield return 0.1f;
                Vector3 aim3 = aim + t.right * 0.1f - Vector3.up * 0.08f;
                GlassBreak.Ray(front, (aim3 - front).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                yield return 0.1f;
                From(front, aim, "wind_3_rounds", 28f);

                Vector3 sideEye = t.position - t.right * 4f + t.forward * 0.1f + Vector3.up * 1.3f;
                Vector3 sideAim = t.position - t.right * 0.8f + t.forward * 0.25f + Vector3.up * 1.12f;
                From(sideEye, sideAim, "side_0_whole", 32f);
                GlassBreak.Ray(sideEye, (sideAim - sideEye).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                yield return 0.1f;
                From(sideEye, sideAim, "side_1_falling", 32f);
                yield return 2.5f;
                From(sideEye, sideAim, "side_2_after", 32f);
                var windows = car.windows;
                Require(windows != null && windows.Length >= 5, "merged car knows its windows: " + (windows != null ? windows.Length : 0));

                // A house window: a web, then it comes apart from where it was first struck.
                var field = GlassField.Instance;
                Require(field != null, "glass field in the scene");
                if (field != null)
                {
                    GlassField.Pane best = default; float bestD = float.MaxValue;
                    Vector3 me = _motor.transform.position;
                    foreach (var p in field.panes)
                    {
                        if (p.kind != GlassField.Kind.Window || p.corner.y < 2.5f) continue;
                        float d = (p.corner - me).sqrMagnitude;
                        if (d >= bestD) continue;
                        // One that can be seen: open air before it, a window that is not behind anything.
                        Vector3 m0 = p.corner + (p.u + p.v) * 0.5f, e0 = m0 + p.normal * 3.2f + Vector3.up * -0.2f;
                        if (Physics.Linecast(e0, m0 + p.normal * 0.05f) || Physics.CheckSphere(e0, 0.4f)) continue;
                        bestD = d; best = p;
                    }
                    Vector3 mid = best.corner + (best.u + best.v) * 0.5f;
                    Vector3 eye = mid + best.normal * 3.2f + Vector3.up * -0.2f;
                    From(eye, mid, "pane_0_whole", 42f);
                    Vector3 point = mid + best.u * 0.14f + best.v * 0.1f;
                    field.Ray(eye, (point - eye).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                    yield return 0.08f;
                    From(eye, mid, "pane_1_cracked", 42f);
                    Vector3 point2 = mid - best.u * 0.2f;
                    field.Ray(eye, (point2 - eye).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                    yield return 0.2f;
                    From(eye, mid, "pane_2_breaking", 42f);
                    yield return 0.25f;
                    From(eye, mid, "pane_3_falling", 42f);
                    yield return 2.5f;
                    From(eye, mid, "pane_4_after", 42f);
                }
            }

            // ---- driving --------------------------------------------------------------
            var cars = Object.FindObjectsByType<DrivableCar>(FindObjectsSortMode.None);
            Require(cars.Length >= 1, "drivable cars: " + cars.Length);
            if (cars.Length == 0) yield break;
            var drive = cars.OrderBy(c => (c.transform.position - _motor.transform.position).sqrMagnitude).First();
            var carT = drive.transform;
            Note($"driving {drive.name} at {carT.position}, mass {drive.mass}, driven {drive.driven}");

            // Stood beside it and in.
            var cc = _motor.GetComponent<CharacterController>();
            cc.enabled = false; _motor.transform.position = carT.TransformPoint(drive.exitOffset) + Vector3.up * 0.1f; cc.enabled = true;
            yield return 0.3f;
            From(carT.TransformPoint(new Vector3(-5f, 1.4f, 3.5f)), carT.position + Vector3.up * 0.8f, "drive_car_outside");
            Private(typeof(DrivableCar), "GetIn").Invoke(drive, null);
            yield return 0.25f;
            From(carT.TransformPoint(new Vector3(-4.5f, 1.5f, 1f)), carT.position + Vector3.up * 0.8f, "drive_door_open");
            yield return 0.8f;
            Require(DrivableCar.Driving == drive, "in the car");
            var view = Camera.main;
            Save(view, "seat_1_rest");
            yield return 0.1f;

            // A long straight of the town's roads, from the traffic routes: the car by the start faces a roundabout 70 m on.
            Vector3 runFrom = carT.position, runDir = carT.forward; float longest = 0f;
            foreach (var r in traffic.routes)
                for (int i = 0; i + 1 < r.points.Length; i++)
                {
                    // Consecutive points running the same way count as one stretch.
                    int k = i; Vector3 d0 = (r.points[i + 1] - r.points[i]).normalized;
                    while (k + 2 < r.points.Length && Vector3.Dot((r.points[k + 2] - r.points[k + 1]).normalized, d0) > 0.999f) k++;
                    float len = Vector3.Distance(r.points[i], r.points[k + 1]);
                    if (len > longest) { longest = len; runFrom = r.points[i]; runDir = d0; }
                }
            Note($"test straight: {longest:0} m from {runFrom}");
            var body = drive.GetComponent<Rigidbody>();
            body.position = runFrom + Vector3.up * 0.3f; body.rotation = Quaternion.LookRotation(new Vector3(runDir.x, 0f, runDir.z)); body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            carT.SetPositionAndRotation(body.position, body.rotation);
            yield return 1.5f;
            Note($"at rest: speed {drive.Kmh:0.0} km/h, tilt {Vector3.Angle(carT.up, Vector3.up):0.0}, wheel on ground {drive.frontLeft.isGrounded}");

            // Off the line: full throttle, straight.
            MobileInput.Move = new Vector2(0f, 1f);
            float t0 = Time.time; float to60 = -1f; int topGear = 1; Vector3 p0 = carT.position; float maxRpm = 0f; float tipped = 0f;
            for (int i = 0; i < 60 * 7; i++)
            {
                if (to60 < 0f && drive.Kmh >= 60f) to60 = Time.time - t0;
                topGear = Mathf.Max(topGear, drive.Gear);
                maxRpm = Mathf.Max(maxRpm, drive.Rpm);
                tipped = Mathf.Max(tipped, Vector3.Angle(carT.up, Vector3.up));
                if (i == 60 * 3) Save(view, "seat_2_moving");
                yield return null;
            }
            Note($"7 s full throttle: {drive.Kmh:0} km/h, 0-60 km/h in {to60:0.0} s, gear {topGear}, peak {maxRpm:0} rpm, {(carT.position - p0).magnitude:0} m, most tilt {tipped:0.0}");
            Require(drive.Kmh > 40f || longest < 120f, "it pulls");
            Require(topGear >= 2, "it changes gear");
            Require(tipped < 12f, "stays on its wheels");

            // Full lock one way then the other at speed, as keys would: a lane change, kept short enough to stay on the road.
            System.Action BackToStart = () =>
            {
                body.position = runFrom + Vector3.up * 0.3f; body.rotation = Quaternion.LookRotation(new Vector3(runDir.x, 0f, runDir.z)); body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                carT.SetPositionAndRotation(body.position, body.rotation);
            };
            float yaw0 = carT.eulerAngles.y; float before = drive.Kmh;
            float peakSlip = 0f, worst = 0f, mostTurned = 0f;
            for (int i = 0; i < 90; i++)
            {
                MobileInput.Move = new Vector2(i < 40 ? -1f : 1f, 0.6f);
                float side = Mathf.Abs(Vector3.Dot(body.linearVelocity, carT.right));
                peakSlip = Mathf.Max(peakSlip, side);
                worst = Mathf.Max(worst, Vector3.Angle(carT.up, Vector3.up));
                mostTurned = Mathf.Max(mostTurned, Mathf.Abs(Mathf.DeltaAngle(yaw0, carT.eulerAngles.y)));
                if (i == 30) Save(view, "seat_3_turning");
                yield return null;
            }
            Note($"lane change from {before:0} km/h: most turned {mostTurned:0} degrees, worst sideways {peakSlip:0.0} m/s, tilt {worst:0.0}, now {drive.Kmh:0} km/h");
            Require(worst < 15f, "corners without tipping");
            Require(peakSlip < 6f, "corners without a spin");
            Require(mostTurned > 4f, "it turns");
            Require(drive.Kmh > before * 0.5f, "a lane change does not stop it");
            MobileInput.Move = Vector2.zero;
            yield return 0.3f;
            BackToStart();
            yield return 1f;

            // The town's traffic, close to.
            var moving = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None);
            Note("traffic cars: " + moving.Length);
            if (moving.Length > 0)
            {
                var tc = moving[0].transform;
                From(tc.position + tc.right * 6f + tc.forward * 3f + Vector3.up * 1.6f, tc.position + Vector3.up * 0.9f, "traffic");
            }
            // The gun: up by firing, the car keeps going with no key down.
            MobileInput.Move = new Vector2(0f, 1f);
            yield return 3f;
            MobileInput.Move = Vector2.zero;
            float cruiseFrom = drive.Kmh;
            var gun = Object.FindAnyObjectByType<Weapon>();      // under the camera, which is off the player while he sits in a car
            float lastShot = gun != null ? gun.LastShotTime : 0f;
            MobileInput.SetFireButton(true);
            yield return 0.7f;
            Require(DrivableCar.GunUp, "the gun came up");
            Save(view, "gun_1_firing");
            Require(gun != null && gun.LastShotTime > lastShot, "it fired");
            MobileInput.SetFireButton(false);
            yield return 2f;
            Note($"gun up, no key down: {cruiseFrom:0} km/h then, {drive.Kmh:0} km/h 2.7 s later, cruise set at {drive.CruiseSpeed * 3.6f:0}");
            Require(cruiseFrom > 15f, "it was moving when the gun came up: " + cruiseFrom.ToString("0"));
            Require(drive.Kmh > cruiseFrom * 0.8f, "the car kept going");
            Save(view, "gun_2_cruising");
            var glass = drive.GetComponent<CarGlass>();
            Note("windows on the driven car: " + (glass != null && glass.windows != null ? glass.windows.Length : 0));
            // Brakes.
            MobileInput.Move = new Vector2(0f, -1f);
            float brakeFrom = drive.Kmh, stopT = Time.time; Vector3 stopP = carT.position;
            for (int i = 0; i < 60 * 8 && drive.CruiseSpeed > 0.1f; i++) yield return null;
            for (int i = 0; i < 60 * 4 && drive.Kmh > 2f; i++) yield return null;
            Note($"from {brakeFrom:0} km/h to a stop in {Time.time - stopT:0.0} s, {(carT.position - stopP).magnitude:0} m");
            MobileInput.Move = Vector2.zero;
            yield return 0.5f;
            typeof(DrivableCar).GetMethod("SetGun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(drive, new object[] { false });
            yield return 0.6f;
            Save(view, "seat_4_hands_back");

            // Out.
            Private(typeof(DrivableCar), "AskOut").Invoke(drive, null);
            yield return 1.2f;
            Require(DrivableCar.Driving == null, "got out");
            Require(PlayerMotor.InputEnabled, "walking again");
            Require(Vector3.Distance(_motor.transform.position, carT.position) < 7f, "stood beside the car");
            Note("drive done");
        }
    }
}
#endif
