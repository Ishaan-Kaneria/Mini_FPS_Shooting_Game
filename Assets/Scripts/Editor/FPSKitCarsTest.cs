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

        /// <summary>Cubes alive, not moving, and not on the ground: frozen in the air.</summary>
        static int HangingCubes()
        {
            int stuck = 0;
            var buffer = new ParticleSystem.Particle[1000];
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name != "Cubes") continue;
                int n = ps.GetParticles(buffer);
                for (int i = 0; i < n; i++)
                {
                    if (buffer[i].velocity.sqrMagnitude > 0.2f * 0.2f) continue;
                    if (buffer[i].position.y > GlassFX.Floor(buffer[i].position) + 0.25f) stuck++;
                }
            }
            return stuck;
        }

        // ---- the script ---------------------------------------------------------------
        static IEnumerator Script()
        {
            yield return 6f;
            Note("level running; player at " + _motor.transform.position);

            // Which parked cars there are.
            var parked = Object.FindObjectsByType<CarGlass>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(g => g.transform.parent != null && g.transform.parent.name.StartsWith("Cars_")).ToArray();
            foreach (var g in parked.Where(g => g.transform.parent.name == "Cars_Pack").Take(4))
                From(g.transform.position + g.transform.parent.forward * 6.5f + g.transform.parent.right * 1.5f + Vector3.up * 1.7f, g.transform.parent.position + Vector3.up * 0.7f, "pack_" + g.name);
            var byKind = parked.GroupBy(g => g.transform.parent.name.Substring(5)).ToDictionary(g => g.Key, g => g.ToArray());
            Note("parked: " + string.Join(", ", byKind.Select(k => $"{k.Key} {k.Value.Length}")));
            Require(byKind.Count >= 7, "our own kinds and the hand-made ones parked");
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
                // Nothing left hanging: every cube has fallen or is on the ground. Measured against the ground under the car, cars left out.
                yield return 2f;
                Require(HangingCubes() == 0, "no glass left in the air after a side window: " + HangingCubes());
                // A later burst, long after the first, on a system that has been idle: it must fall as well.
                yield return 4f;
                Vector3 far = t.position + t.right * 4f + t.forward * -0.2f + Vector3.up * 1.3f;
                GlassBreak.Ray(far, ((t.position + t.right * 0.8f - t.forward * 0.25f + Vector3.up * 1.12f) - far).normalized, 8f, GlassField.Blow.Round, _motor.gameObject);
                yield return 0.1f;
                From(far, t.position + Vector3.up * 1.1f, "side_3_second_falling", 32f);
                yield return 4f;
                From(far, t.position + Vector3.up * 0.7f, "side_4_second_after", 32f);
                Require(HangingCubes() == 0, "no glass left in the air after a later burst: " + HangingCubes());
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

            // Backing up: Down and Left, the nose goes left, as it looks from the seat. Then the other way.
            MobileInput.Move = new Vector2(0f, -1f);
            for (int i = 0; i < 60 * 4 && drive.Kmh > 1f; i++) yield return null;
            BackToStart();
            yield return 0.6f;
            foreach (float side in new[] { -1f, 1f })
            {
                float y0 = carT.eulerAngles.y;
                MobileInput.Move = new Vector2(side, -1f);
                yield return 2f;
                float turned = Mathf.DeltaAngle(y0, carT.eulerAngles.y);
                Note($"reverse with {(side < 0f ? "left" : "right")} for 2 s: {drive.Kmh:0} km/h backwards (speed along the car {Vector3.Dot(body.linearVelocity, carT.forward):0.0} m/s), nose turned {turned:0} degrees");
                Require(Vector3.Dot(body.linearVelocity, carT.forward) < -2f, "it goes backwards");
                Require(Mathf.Sign(turned) == side && Mathf.Abs(turned) > 5f, "the nose goes the way the key says");
                Require(Mathf.Abs(turned) < 85f, "backing up does not spin it");
                if (side < 0f) Save(view, "reverse_left");
                MobileInput.Move = Vector2.zero;
                BackToStart();
                yield return 0.8f;
            }

            // A drift: up to speed, handbrake and full lock, held, then let go and steered straight.
            MobileInput.Move = new Vector2(0f, 1f);
            yield return 5f;
            float driftFromKmh = drive.Kmh, y1 = carT.eulerAngles.y, mostSlip = 0f, tiltDrift = 0f;
            MobileInput.Handbrake = true;
            MobileInput.Move = new Vector2(-1f, 0.7f);
            for (int i = 0; i < 66; i++)
            {
                if (body.linearVelocity.magnitude > 2f)
                    mostSlip = Mathf.Max(mostSlip, Vector3.Angle(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up), Vector3.ProjectOnPlane(carT.forward, Vector3.up)));
                tiltDrift = Mathf.Max(tiltDrift, Vector3.Angle(carT.up, Vector3.up));
                if (i == 40) Save(view, "drift_seat");
                if (i == 50) { var from = carT.position + carT.right * 9f + Vector3.up * 3f; From(from, carT.position, "drift_outside"); }
                yield return null;
            }
            float afterDrift = drive.Kmh, turnedDrift = Mathf.DeltaAngle(y1, carT.eulerAngles.y);
            MobileInput.Handbrake = false;
            MobileInput.Move = new Vector2(0f, 0.4f);
            // Frame by frame while it is really moving: past that it has met the fence, the road being twenty metres wide and the car now across it.
            float settled = 180f, settledAt = -1f; var curve = new StringBuilder();
            for (int i = 0; i < 96; i++)
            {
                float v = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
                float off = Vector3.Angle(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up), Vector3.ProjectOnPlane(carT.forward, Vector3.up));
                if (i % 12 == 0) curve.Append($" {i / 60f:0.0}s:{off:0}deg@{v:0.0}");
                if (v > 3f && off < settled) { settled = off; settledAt = i / 60f; }
                yield return null;
            }
            Note("after letting go:" + curve + $"; straightest {settled:0} degrees at {settledAt:0.0} s");
            Note($"drift from {driftFromKmh:0} km/h: most angle {mostSlip:0} degrees, turned {turnedDrift:0}, {afterDrift:0} km/h at the end, tilt {tiltDrift:0.0}; 1.6 s after letting go, {settled:0} degrees off its heading");
            Require(mostSlip > 20f, "the back steps out");
            Require(mostSlip < 58f, "a slide, not a spin");
            Require(afterDrift > driftFromKmh * 0.55f, "it keeps its speed through a slide");
            Require(Mathf.Abs(turnedDrift) > 30f, "it goes round the corner");
            Require(settled < 12f, "it straightens after");
            Require(tiltDrift < 18f, "stays on its wheels in a drift");
            MobileInput.Move = Vector2.zero;
            BackToStart();
            yield return 1f;

            // A soldier walking into the car, with and without the rule (what threw the car over).
            var soldiers = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Where(e => e.TryGetComponent<Health>(out var eh) && !eh.IsDead && e.isActiveAndEnabled).ToArray();
            Note("soldiers alive: " + soldiers.Length);
            int vehicleLayer = LayerMask.NameToLayer("Vehicle"), enemyLayer = LayerMask.NameToLayer("Enemy");
            for (int trial = 0; trial < 2; trial++)
            {
                if (soldiers.Length == 0) break;
                bool rule = trial == 1;
                Physics.IgnoreLayerCollision(vehicleLayer, enemyLayer, rule);
                BackToStart();
                yield return 1f;
                float peakTilt = 0f, peakUp = 0f, peakSpin = 0f;
                for (int i = 0; i < 60 * 3; i++)
                {
                    // Stood in the car's side and under its front wheel, as a man pressing against it is.
                    var at = i % 40 < 20 ? carT.TransformPoint(new Vector3(0.75f, 0f, 1.3f)) : carT.TransformPoint(new Vector3(-0.2f, 0f, 0.4f));
                    for (int k = 0; k < Mathf.Min(4, soldiers.Length); k++)
                    {
                        if (soldiers[k] == null) continue;
                        var agent = soldiers[k].GetComponent<UnityEngine.AI.NavMeshAgent>();
                        if (agent != null && agent.enabled) agent.Warp(at + carT.forward * (k * 0.5f));
                        else soldiers[k].transform.position = at;
                    }
                    peakTilt = Mathf.Max(peakTilt, Vector3.Angle(carT.up, Vector3.up));
                    peakUp = Mathf.Max(peakUp, body.linearVelocity.y);
                    peakSpin = Mathf.Max(peakSpin, body.angularVelocity.magnitude);
                    yield return null;
                }
                Note($"soldiers pressed into the car, {(rule ? "people do not collide" : "CONTROL, people collide")}: most tilt {peakTilt:0.0}, fastest upward {peakUp:0.0} m/s, fastest spin {peakSpin:0.0} rad/s");
                if (rule) { Require(peakTilt < 6f, "soldiers do not tip the car"); Require(peakUp < 0.6f, "soldiers do not lift the car"); }
            }
            foreach (var sold in soldiers) if (sold != null && sold.TryGetComponent<Health>(out var sh)) { }
            // A blast beside it.
            BackToStart();
            yield return 1f;
            drive.Blasted(carT.TransformPoint(new Vector3(2f, 0.3f, 0f)), 6f);
            float blastTilt = 0f;
            for (int i = 0; i < 120; i++) { blastTilt = Mathf.Max(blastTilt, Vector3.Angle(carT.up, Vector3.up)); yield return null; }
            Note($"a blast 2 m away: most tilt {blastTilt:0.0}");
            Require(blastTilt < 30f, "a blast does not roll it");
            // Tipped over by hand: it comes back onto its wheels.
            body.rotation = Quaternion.Euler(0f, carT.eulerAngles.y, 175f); carT.rotation = body.rotation; body.position += Vector3.up * 1.2f;
            yield return 4f;
            Note($"turned on its roof: 4 s later, tilt {Vector3.Angle(carT.up, Vector3.up):0.0}");
            Require(Vector3.Angle(carT.up, Vector3.up) < 20f, "back on its wheels");
            BackToStart();
            yield return 1f;

            // People and cars (2026-11-09): the town's people have the enemies' bodies; a car hits a person by its speed.
            var civDir = Object.FindAnyObjectByType<CivilianDirector>();
            var civPrefab = civDir != null && civDir.prefabs != null && civDir.prefabs.Length > 0 ? civDir.prefabs[0] : null;
            Require(civPrefab != null, "a civilian prefab to test with");
            if (civPrefab != null)
            {
                var probe = Object.Instantiate(civPrefab, new Vector3(0f, -300f, 0f), Quaternion.identity);
                int boxes = probe.GetComponentsInChildren<Hitbox>().Length;
                Note($"a civilian's hitboxes: {boxes}, ragdoll {(probe.GetComponent<RagdollController>() != null)}");
                Require(boxes >= 10, "a civilian has the enemies' hitboxes");
                Require(probe.GetComponent<RagdollController>() != null, "a civilian has a ragdoll");
                Object.Destroy(probe);
                foreach (float kmh in new[] { 15f, 35f, 70f })
                {
                    BackToStart();
                    yield return 0.6f;
                    traffic.recycleDistance = 100000f;      // the test's player stands far off: nothing here may be put away
                    Vector3 spot = runFrom + runDir * 22f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var onMesh, 3f, UnityEngine.AI.NavMesh.AllAreas)) spot = onMesh.position;
                    var man = Object.Instantiate(civPrefab, spot, Quaternion.LookRotation(-runDir));
                    var mc = man.GetComponent<Civilian>(); var mh = man.GetComponent<Health>();
                    var hips = man.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips);
                    yield return 0.5f;
                    Vector3 flatDir = new Vector3(runDir.x, 0f, runDir.z).normalized;
                    float hitAt = -1f, maxUp = 0f, maxFar = 0f; bool shot = false;
                    for (int i = 0; i < 60 * 10; i++)
                    {
                        if (hitAt < 0f)
                        {
                            body.linearVelocity = flatDir * kmh / 3.6f + Vector3.up * body.linearVelocity.y;
                            if (mh.IsDead || mh.Current < mh.maxHealth || mc.Current == Civilian.State.Down) { hitAt = Time.time; MobileInput.Move = new Vector2(0f, -1f); }
                        }
                        else if (hips != null)
                        {
                            maxUp = Mathf.Max(maxUp, hips.position.y - spot.y);
                            maxFar = Mathf.Max(maxFar, Vector3.ProjectOnPlane(hips.position - spot, Vector3.up).magnitude);
                            if (!shot && Time.time - hitAt > 0.3f) { shot = true; From(spot - Vector3.Cross(Vector3.up, flatDir) * 9f + flatDir * 4f + Vector3.up * 1.6f, spot + flatDir * 4f + Vector3.up * 0.8f, $"hit_{kmh:0}kmh"); }
                        }
                        yield return null;
                    }
                    MobileInput.Move = Vector2.zero;
                    Note($"hit at {kmh:0} km/h: {(hitAt > 0f ? "hit" : "MISSED")}, health {mh.Current:0}, {(mh.IsDead ? "dead" : mc.Current.ToString())}, thrown {maxFar:0.0} m, hips up to {maxUp:0.0} m, limping {mc.Limping}");
                    Require(hitAt > 0f, $"the car hit him at {kmh:0} km/h");
                    if (kmh <= 15f) Require(!mh.IsDead && mc.Current != Civilian.State.Down, "slow: knocked down and up again");
                    if (kmh >= 70f) Require(mh.IsDead && maxFar > 6f, "fast: thrown and dead");
                    if (hitAt > 0f && !mh.IsDead && mc.Current != Civilian.State.Down) From(hips.position - Vector3.Cross(Vector3.up, flatDir) * 4f + Vector3.up * 1.2f, hips.position, $"hit_{kmh:0}kmh_after");
                    Object.Destroy(man);
                }
            }

            // A traffic car hit side on at 40 km/h: shoved, turned and dented, not a wall.
            var victim = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).FirstOrDefault(c => c.isActiveAndEnabled && !c.Crashed);
            Require(victim != null, "a traffic car to hit");
            if (victim != null)
            {
                traffic.recycleDistance = 100000f;
                BackToStart();
                typeof(TrafficCar).GetField("route", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(victim, -1);     // off its route: it stands
                var vt = victim.transform;
                Vector3 at = runFrom + new Vector3(runDir.x, 0f, runDir.z).normalized * 30f;
                var side = Quaternion.LookRotation(Vector3.Cross(Vector3.up, new Vector3(runDir.x, 0f, runDir.z)));
                vt.SetPositionAndRotation(at, side);
                // The physics body too: an interpolated kinematic body put its own pose back over the transform's (the first try left it 100 m off).
                if (victim.TryGetComponent<Rigidbody>(out var vrb)) { vrb.position = at; vrb.rotation = side; }
                yield return 0.6f;
                Vector3 vFrom = vt.position; float yawFrom = vt.eulerAngles.y;
                Vector3 flat2 = new Vector3(runDir.x, 0f, runDir.z).normalized;
                float myBefore = 0f, closest = float.MaxValue;
                var vCols = victim.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                Note($"crash setup: victim at {vt.position}, layer {LayerMask.LayerToName(victim.gameObject.layer)}, colliders {vCols.Length} ({string.Join(",", vCols.Select(c => LayerMask.LayerToName(c.gameObject.layer)).Distinct())}), kinematic {victim.GetComponent<Rigidbody>()?.isKinematic}; my car at {carT.position}");
                for (int i = 0; i < 60 * 5; i++)
                {
                    if (!victim.Crashed) { body.linearVelocity = flat2 * 40f / 3.6f + Vector3.up * body.linearVelocity.y; myBefore = drive.Kmh; }
                    else MobileInput.Move = new Vector2(0f, -1f);
                    closest = Mathf.Min(closest, Vector3.ProjectOnPlane(carT.position - vt.position, Vector3.up).magnitude);
                    yield return null;
                }
                Note($"closest the two cars came: {closest:0.0} m; my car now at {carT.position}");
                MobileInput.Move = Vector2.zero;
                var dents = victim.GetComponent<CarDents>();
                var own = dents != null ? (System.Collections.IDictionary)typeof(CarDents).GetField("_own", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dents) : null;
                Note($"traffic car hit at {myBefore:0} km/h: crashed {victim.Crashed}, moved {(vt.position - vFrom).magnitude:0.0} m, turned {Mathf.Abs(Mathf.DeltaAngle(yawFrom, vt.eulerAngles.y)):0} degrees, dented meshes {(own != null ? own.Count : 0)}, tilt {Vector3.Angle(vt.up, Vector3.up):0}");
                Require(victim.Crashed, "the traffic car became a physics car");
                Require((vt.position - vFrom).magnitude > 1f, "it was shoved");
                Require(own != null && own.Count > 0, "it was dented");
                From(vt.position - Vector3.Cross(Vector3.up, flat2) * 0.1f - flat2 * 6f + Vector3.up * 1.8f, vt.position + Vector3.up * 0.6f, "crash_traffic");
                From(carT.position + flat2 * 6f + Vector3.up * 1.6f, carT.position + Vector3.up * 0.6f, "crash_mine");
                var myDents = drive.GetComponent<CarDents>();
                var mine = myDents != null ? (System.Collections.IDictionary)typeof(CarDents).GetField("_own", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(myDents) : null;
                Note($"my car's dented meshes: {(mine != null ? mine.Count : 0)}");
                Object.Destroy(victim.gameObject);      // the wreck lay across the road the rest of the drive takes
                BackToStart();
                yield return 0.5f;
            }

            // Parked cars are cut out of the navmesh: people walk round them.
            int parkedObstacles = Object.FindObjectsByType<UnityEngine.AI.NavMeshObstacle>(FindObjectsSortMode.None).Count(o => o.carving && o.transform.parent != null && o.transform.parent.name == "ParkedCars");
            Note($"parked cars cut out of the navmesh: {parkedObstacles}");
            Require(parkedObstacles > 50, "parked cars are obstacles");

            // The town's traffic, close to.
            var moving = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None).Where(c => !c.Crashed).ToArray();
            Note("traffic cars: " + moving.Length);
            if (moving.Length > 0)
            {
                var tc = moving[0].transform;
                From(tc.position + tc.right * 6f + tc.forward * 3f + Vector3.up * 1.6f, tc.position + Vector3.up * 0.9f, "traffic");
                // Its driver close to, through the side window: hands on the wheel, head on the road (2026-11-09).
                var car0 = moving[0];
                traffic.recycleDistance = 100000f;      // the test's player is far away: the watched car must stay
                var driver0 = car0.GetComponentInChildren<VehicleDriver>();
                Require(driver0 != null, "the traffic car has a driver");
                if (driver0 != null)
                {
                    // The test's own camera is not the player's: his pose is forced on, as the player's view would have it.
                    var anim0 = driver0.GetComponentInChildren<Animator>();
                    if (anim0 != null) anim0.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    VehicleDriver.AlwaysPose = true;
                    yield return 0.8f;
                    var head0 = driver0.GetComponentInChildren<Animator>()?.GetBoneTransform(HumanBodyBones.Head);
                    Vector3 face = head0 != null ? head0.position : driver0.transform.position + Vector3.up * 1f;
                    From(face - tc.right * 1.6f + tc.forward * 0.5f + Vector3.up * 0.15f, face - Vector3.up * 0.25f, "driver_1_window");
                    From(face + tc.forward * 2.6f + Vector3.up * 0.3f, face - Vector3.up * 0.2f, "driver_2_windscreen");
                    // Low at his side and from the front corner: nothing of him through the doors or the floor.
                    From(tc.position - tc.right * 3.2f + tc.forward * 0.4f + Vector3.up * 0.7f, tc.position + Vector3.up * 0.5f, "driver_3_side_low");
                    From(tc.position + tc.forward * 3.5f - tc.right * 2.5f + Vector3.up * 0.6f, tc.position + Vector3.up * 0.5f, "driver_4_front_corner");
                    // Getting out: stopped by gunfire, the door, the turn, standing up out of the car.
                    typeof(TrafficCar).GetField("_panicUntil", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car0, Time.time + 60f);
                    typeof(TrafficCar).GetField("_danger", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(car0, tc.position + tc.forward * 30f);
                    for (int i = 0; i < 60 * 5 && car0.Speed > 0.5f; i++) yield return null;
                    Private(typeof(TrafficCar), "LeaveCar").Invoke(car0, null);
                    Vector3 watch = tc.position - tc.right * 5f + tc.forward * 1.5f + Vector3.up * 1.7f;
                    int Near() { int n = 0; foreach (var c in Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None)) if ((c.transform.position - tc.position).magnitude < 4f) n++; return n; }
                    yield return 0.3f; From(watch, tc.position + Vector3.up * 0.9f, "exit_1_door");
                    yield return 0.4f; From(watch, tc.position + Vector3.up * 0.9f, "exit_2_turning");
                    yield return 0.3f; From(watch, tc.position + Vector3.up * 0.9f, "exit_3_stepping");
                    Note($"people within 4 m of the car just after he got out: {Near()}");
                    yield return 0.6f; From(watch, tc.position + Vector3.up * 0.9f, "exit_4_out");
                    Note($"driver got out: driver left in the seat {car0.GetComponentInChildren<VehicleDriver>() != null}, abandoned {car0.Abandoned}");
                }
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
