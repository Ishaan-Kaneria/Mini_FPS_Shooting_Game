#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// A dead enemy's gun lies on the ground, not over it (Ishaan, 2026-10-18: "enemy guns hovering
    /// above the ground after enemies die").
    ///
    /// Two enemies are killed in the AI test scene, one rigged (its rifle let go by
    /// <c>EnemyRifle.Drop</c>) and one with its rifle rig switched off (the plain
    /// <c>EnemyBody.DropGun</c> path). Each gun is measured where its death left it, then dropped
    /// again onto concrete (a box), wood (a mesh collider), sand (a terrain) and slopes of 15 and 26
    /// degrees (box and terrain), in six orientations. Once it has settled: the gap between the
    /// lowest point of the visible mesh and the surface under it (along the surface's normal), and
    /// how far it still moves over the next second.
    ///
    /// Pass, for every settled gun: its lowest drawn point within 1 cm of the surface and not more
    /// than 1 cm into it; its main part (receiver, barrel and stock) within 1 cm too, so it is not
    /// propped on one protruding piece over an invisible shelf; its centre of mass over the drawn
    /// points that touch; and moving under 2 mm. With a graphics device it also renders close-ups into Build/GunDrop.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyGunDrop                 (numbers)
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.VerifyGunDrop (and the pictures)
    /// </summary>
    public static class FPSKitGunDropTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/AITest.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Build/GunDrop";
        const float MaxGap = 0.01f, MaxSink = 0.01f, MaxJitter = 0.002f;
        /// <summary>Metres the centre of mass may sit outside the drawn contact points (a rifle on its side balances on a line).</summary>
        const float MaxOffSupport = 0.01f;

        static readonly Vector3[] Orientations =
        {
            new Vector3(0f, 0f, 90f),     // on its left side
            new Vector3(0f, 0f, -90f),    // on its right side
            new Vector3(0f, 0f, 0f),      // upright, on the magazine and grip
            new Vector3(180f, 0f, 0f),    // upside down
            new Vector3(45f, 30f, 0f),    // muzzle down
            new Vector3(-60f, 90f, 20f),  // stock down, turned
        };

        class Surface { public string name; public Collider collider; public Vector3 top; }
        class Gun { public string name; public Transform t; public Rigidbody rb; public List<Surface> row; }

        public static bool Running { get; private set; }

        static readonly List<string> Lines = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly List<Gun> Guns = new List<Gun>();
        static int _step, _lastFrame, _drop, _shots;
        static float _t0, _stillSince;
        static double _realStart;
        static GameObject _rigged, _plain;
        static readonly Dictionary<Gun, List<Vector3>> Track = new Dictionary<Gun, List<Vector3>>();

        [MenuItem("FPSKit/Enemy AI/Run Gun Drop Test")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            if (!File.Exists(ScenePath)) FPSKitAITestScene.Build();
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Lines.Clear(); Fails.Clear(); Guns.Clear(); Track.Clear();
            _step = 0; _lastFrame = -1; _drop = 0; _shots = 0;
            _realStart = EditorApplication.timeSinceStartup;
            Running = true;
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        public static void Batch() => Run();

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _realStart > 900) throw new Exception("gun drop test timed out");
                if (!EditorApplication.isPlaying) return;
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 60f;

                switch (_step)
                {
                    case 0: Setup(); _t0 = Time.time; _step = 1; return;
                    case 1:   // let them stand, then kill them
                        if (Time.time - _t0 < 1f) return;
                        Kill(_rigged); Kill(_plain);
                        _t0 = Time.time; _stillSince = -1f; _step = 2;
                        return;
                    case 2:   // where the death left the guns
                        if (!Settled(6f)) return;
                        foreach (var g in Guns) Measure(g, "death drop", null);
                        Shoot("death_drop", Guns[0], 1.2f);
                        _step = 3; _drop = 0;
                        return;
                    case 3:   // the next drop onto every row
                    {
                        int surfaces = Guns[0].row.Count;
                        if (_drop >= surfaces * Orientations.Length) { _step = 6; return; }
                        int s = _drop / Orientations.Length, o = _drop % Orientations.Length;
                        foreach (var g in Guns) Release(g, g.row[s], Orientations[o]);
                        _t0 = Time.time; _stillSince = -1f; _step = 4;
                        return;
                    }
                    case 4:
                        if (!Settled(8f)) return;
                        foreach (var g in Guns) Track[g] = new List<Vector3>();
                        _t0 = Time.time; _step = 5;
                        return;
                    case 5:   // a second more, for jitter
                        foreach (var g in Guns) Track[g].Add(g.t.position);
                        if (Time.time - _t0 < 1f) return;
                        {
                            int s = _drop / Orientations.Length, o = _drop % Orientations.Length;
                            foreach (var g in Guns)
                            {
                                Measure(g, $"{g.row[s].name} {Orientations[o]}", Track[g]);
                                if (o == 1 && g == Guns[0]) Shoot($"{g.row[s].name}_right_side", g, 0.9f);
                            }
                        }
                        _drop++; _step = 3;
                        return;
                    case 6: Finish(); return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fails.Add("error: " + e.Message);
                Finish();
            }
        }

        // ------------------------------------------------------------------

        static void Setup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (prefab == null) throw new Exception(EnemyPrefab + " is missing");

            _rigged = Spawn(prefab, new Vector3(-2f, 0f, 10f), "Rigged");
            _plain = Spawn(prefab, new Vector3(2f, 0f, 10f), "Plain");
            // The plain path: the rifle rig off before it ever starts, so EnemyBody drops the gun itself.
            var rifle = _plain.GetComponent<EnemyRifle>();
            if (rifle != null) rifle.enabled = false;

            Guns.Add(new Gun { name = "rigged", t = _rigged.GetComponent<EnemyBody>().gun, row = Row(0f) });
            Guns.Add(new Gun { name = "plain", t = _plain.GetComponent<EnemyBody>().gun, row = Row(-7f) });
            foreach (var g in Guns)
            {
                if (g.t == null) throw new Exception($"the {g.name} enemy has no gun");
                Lines.Add($"  {g.name} gun '{g.t.name}', scale {g.t.lossyScale.x:0.000}: visible mesh {Describe(VisibleBounds(g.t))} (gun space)");
                foreach (var mf in g.t.GetComponentsInChildren<MeshFilter>(true))
                {
                    var r = mf.GetComponent<Renderer>();
                    bool seen = r != null && r.enabled && mf.gameObject.activeInHierarchy;
                    Lines.Add($"      mesh '{mf.name}'{(seen ? "" : " (not drawn)")}: {Describe(LocalBounds(g.t, mf))}");
                }
            }
        }

        static GameObject Spawn(GameObject prefab, Vector3 at, string name)
        {
            var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(Vector3.back));
            go.name = name;
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(go);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.enabled = false;
            return go;
        }

        static void Kill(GameObject enemy)
        {
            var h = enemy.GetComponent<Health>();
            var p = enemy.transform.position + Vector3.up * 1.3f;
            h.ApplyDamage(new DamageInfo(h.maxHealth + h.maxShield + 100f, p, Vector3.back, Vector3.forward, null));
        }

        /// <summary>A row of test grounds along x at this z: concrete, wood, sand, two slopes, a sand slope.</summary>
        static List<Surface> Row(float z)
        {
            var row = new List<Surface>
            {
                Box("concrete", new Vector3(14f, 0f, z), 0f, "Concrete", mesh: false),
                Box("wood", new Vector3(19f, 0f, z), 0f, "Wood", mesh: true),
                Ground("sand", new Vector3(24f, 0f, z), 0f),
                Box("slope15", new Vector3(29f, 0f, z), 15f, "Concrete", mesh: false),
                Box("slope26", new Vector3(34f, 0f, z), 26f, "Concrete", mesh: false),
                Ground("sand_slope20", new Vector3(39f, 0f, z), 20f),
            };
            return row;
        }

        static Surface Box(string name, Vector3 at, float slope, string tag, bool mesh)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GunDrop_" + name;
            go.transform.position = at + Vector3.up * 0.6f;
            go.transform.rotation = Quaternion.Euler(slope, 0f, 0f);
            go.transform.localScale = new Vector3(3f, 0.5f, 3f);
            TrySetTag(go, tag);
            Collider c = go.GetComponent<BoxCollider>();
            if (mesh)
            {
                Object.Destroy(c);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                c = mc;
            }
            return new Surface { name = name, collider = c, top = go.transform.position + go.transform.up * 0.25f };
        }

        static Surface Ground(string name, Vector3 at, float slope)
        {
            var data = new TerrainData { heightmapResolution = 33 };
            data.size = new Vector3(4f, 4f, 4f);
            var h = new float[33, 33];
            for (int y = 0; y < 33; y++)
                for (int x = 0; x < 33; x++)
                    h[y, x] = (0.85f + Mathf.Tan(slope * Mathf.Deg2Rad) * (y / 32f * 4f - 2f)) / 4f;
            data.SetHeights(0, 0, h);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "GunDrop_" + name;
            go.transform.position = at - new Vector3(2f, 0f, 2f);
            TrySetTag(go, "Sand");
            return new Surface { name = name, collider = go.GetComponent<TerrainCollider>(), top = at + Vector3.up * 0.85f };
        }

        static void TrySetTag(GameObject go, string tag) { try { go.tag = tag; } catch (UnityException) { } }

        static void Release(Gun g, Surface s, Vector3 euler)
        {
            g.rb = g.t.GetComponent<Rigidbody>();
            if (g.rb == null) throw new Exception($"the {g.name} gun has no rigidbody after the death");
            g.rb.isKinematic = false;
            g.rb.position = s.top + Vector3.up * 0.45f;
            g.rb.rotation = Quaternion.Euler(euler) * Quaternion.Euler(0f, 35f, 0f);
            g.t.SetPositionAndRotation(g.rb.position, g.rb.rotation);
            g.rb.linearVelocity = Vector3.down * 0.5f;
            g.rb.angularVelocity = Vector3.zero;
            g.rb.WakeUp();
        }

        static bool Settled(float timeout)
        {
            bool still = true;
            foreach (var g in Guns)
            {
                g.rb ??= g.t.GetComponent<Rigidbody>();
                if (g.rb == null) continue;
                if (!g.rb.IsSleeping() && (g.rb.linearVelocity.sqrMagnitude > 1e-4f || g.rb.angularVelocity.sqrMagnitude > 4e-4f)) still = false;
            }
            if (!still) _stillSince = -1f;
            else if (_stillSince < 0f) _stillSince = Time.time;
            return (_stillSince > 0f && Time.time - _stillSince > 0.5f) || Time.time - _t0 > timeout;
        }

        /// <summary>The gap between the visible mesh's lowest point and what it rests on, and its jitter.</summary>
        static void Measure(Gun g, string what, List<Vector3> track)
        {
            float gap = float.MaxValue, boxGap = float.MaxValue, bodyGap = float.MaxValue;
            string on = "nothing";
            var contacts = new List<Vector3>();
            Vector3 normal = Vector3.up;

            // The gun's main part (receiver, barrel and stock): the one a floating gun shows.
            MeshFilter main = null;
            float mainSize = 0f;
            foreach (var mf in g.t.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var sz = mf.sharedMesh.bounds.size;
                if (sz.x * sz.y * sz.z > mainSize) { mainSize = sz.x * sz.y * sz.z; main = mf; }
            }

            foreach (var mf in g.t.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 w = mf.transform.TransformPoint(v);
                    if (!Under(g, w, out float d, out string name, out Vector3 n)) continue;
                    if (d < gap) { gap = d; on = name; normal = n; }
                    if (mf == main && d < bodyGap) bodyGap = d;
                    if (d < 0.004f) contacts.Add(w);
                }
            }

            // Resting on what can be seen: the centre of mass over the visible points that touch.
            var rb = g.t.GetComponent<Rigidbody>();
            float offSupport = rb != null ? OffHull(contacts, rb.worldCenterOfMass, normal) : 0f;
            foreach (var c in g.t.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                foreach (var w in Corners(c))
                    if (Under(g, w, out float d, out _, out _) && d < boxGap) boxGap = d;
            }

            float jitter = 0f;
            if (track != null && track.Count > 1)
                foreach (var p in track) jitter = Mathf.Max(jitter, (p - track[0]).magnitude);

            bool ok = gap <= MaxGap && gap >= -MaxSink && bodyGap <= MaxGap && offSupport <= MaxOffSupport && jitter <= MaxJitter;
            if (gap == float.MaxValue) ok = false;
            string line = $"  {g.name,-7} {what,-34} lowest {gap * 1000f,6:0.0}  body {bodyGap * 1000f,6:0.0}  collider {boxGap * 1000f,5:0.0}  off support {offSupport * 1000f,5:0.0}  jitter {jitter * 1000f,4:0.0} (mm)  on {on}{(ok ? "" : "   <-- FAIL")}";
            Lines.Add(line);
            if (!ok) Fails.Add(line.Trim());
        }

        /// <summary>Signed distance from a point down to the first thing under it that is not the gun, along that thing's normal.</summary>
        static bool Under(Gun g, Vector3 w, out float distance, out string name, out Vector3 normal)
        {
            distance = 0f; name = ""; normal = Vector3.up;
            // The first surface over or under the point, except a body lying over the gun (a corpse is
            // not what it rests on). Static ground above the point counts: that is the gun sunk into it.
            var hits = Physics.RaycastAll(w + Vector3.up * 0.6f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(g.t)) continue;
                if (h.point.y > w.y && h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; found = true; distance = Vector3.Dot(w - h.point, h.normal); name = h.collider.name; normal = h.normal; }
            }
            return found;
        }

        /// <summary>
        /// Metres the centre of mass lies outside the outline of the touching points, seen along the
        /// ground's normal: 0 when the gun stands on what is drawn, more when an invisible part of a
        /// collider is holding it up.
        /// </summary>
        static float OffHull(List<Vector3> points, Vector3 com, Vector3 normal)
        {
            if (points.Count == 0) return float.MaxValue;
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            var p = new List<Vector2>();
            foreach (var w in points) p.Add(new Vector2(Vector3.Dot(w, u), Vector3.Dot(w, v)));
            var c = new Vector2(Vector3.Dot(com, u), Vector3.Dot(com, v));

            // Monotone chain hull.
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int floor = hull.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    var q = p[pass == 0 ? i : p.Count - 1 - i];
                    while (hull.Count >= floor + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(q);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            if (hull.Count == 0) hull.Add(p[0]);

            bool inside = hull.Count >= 3;
            for (int i = 0; i < hull.Count && inside; i++)
                if (Cross(hull[i], hull[(i + 1) % hull.Count], c) < 0f) inside = false;
            if (inside) return 0f;

            float best = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count], ab = b - a;
                float t = ab.sqrMagnitude > 1e-9f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - c).magnitude);
            }
            return best;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static IEnumerable<Vector3> Corners(Collider c)
        {
            if (c is BoxCollider b)
            {
                for (int i = 0; i < 8; i++)
                    yield return b.transform.TransformPoint(b.center + Vector3.Scale(b.size * 0.5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            }
            else if (c is MeshCollider m && m.sharedMesh != null && m.sharedMesh.isReadable)
            {
                foreach (var v in m.sharedMesh.vertices) yield return m.transform.TransformPoint(v);
            }
        }

        static Bounds VisibleBounds(Transform gun)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                var lb = LocalBounds(gun, mf);
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            return b;
        }

        static Bounds LocalBounds(Transform gun, MeshFilter mf)
        {
            var b = new Bounds();
            if (mf.sharedMesh == null) return b;
            var m = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = gun.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (i == 0) b = new Bounds(local, Vector3.zero); else b.Encapsulate(local);
            }
            return b;
        }

        static string Describe(Bounds b) => $"centre {b.center.ToString("F3")} size {b.size.ToString("F3")}";

        /// <summary>A low close-up of the gun where it lies (needs a graphics device).</summary>
        static void Shoot(string name, Gun g, float distance)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Directory.CreateDirectory(Folder);
            // Square on to the gun's length, a few centimetres off the ground, where a gap shows.
            var rb = g.t.GetComponent<Rigidbody>();
            Vector3 at = rb != null ? rb.worldCenterOfMass : g.t.position;
            Vector3 along = Vector3.ProjectOnPlane(g.t.forward, Vector3.up);
            Vector3 side = along.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, along.normalized) : Vector3.right;
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out var ground, 3f, ~0, QueryTriggerInteraction.Ignore)) at.y = ground.point.y;
            cam.transform.position = at + side * distance + Vector3.up * 0.06f;
            cam.transform.LookAt(at + Vector3.up * 0.02f);
            cam.fieldOfView = 40f;
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes($"{Folder}/{name}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                _shots++;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            Running = false;
            string report = (Fails.Count == 0 ? "gun drop test passed: every settled gun rests on the surface\n"
                                              : $"gun drop test FAILED ({Fails.Count}):\n")
                            + $"  pass: lowest drawn point and main part within {MaxGap * 1000f:0} mm above and {MaxSink * 1000f:0} mm into the surface, centre of mass within {MaxOffSupport * 1000f:0} mm of the drawn contacts, jitter under {MaxJitter * 1000f:0} mm; {_shots} picture(s) in {Folder}\n"
                            + string.Join("\n", Lines);
            Directory.CreateDirectory(Folder);
            File.WriteAllText($"{Folder}/report.txt", report);
            if (Fails.Count == 0) Debug.Log("[FPSKitBatch] " + report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + report);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
