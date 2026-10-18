#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Does an aware enemy's face point at the player's eyes? (Ishaan, 2026-10-18: "at close range
    /// the enemy's head and upper body turn toward my LEFT side".)
    ///
    /// A Grunt on a floor of the test's own, the player at 1, 3, 10 and 30 m, to its left, its right,
    /// above it (25 degrees up, the player on a block) and below it (the Grunt on the block). The Grunt
    /// is told where the player is and left to fight (its shots do no damage). Every game frame at 60 fps
    /// it measures the angle between the face (the head bone's forward, taken from the avatar's rest
    /// pose, in which the model faces its root's forward) and the line from between the eyes to the
    /// player's camera; and the muzzle's angle off the point it aims at. Frames count once the brain has
    /// the player in sight, the aim is fully up, no strike or reaction owns the body, and 1.2 s have
    /// passed for the turn. Fails when a case's median face error is over <see cref="MaxMedian"/> degrees.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyFace       (Build/FaceTest: report; pictures with a graphics device)
    ///   FPSKit > Enemy AI > Measure Face Direction         (in the open editor)
    /// </summary>
    public static class FPSKitFaceTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Build/FaceTest";
        const float MaxMedian = 5f;
        const float Settle = 1.2f, CaseSeconds = 4.5f;
        const float Floor = 400f;
        const int W = 400, H = 225;

        public static bool Running { get; private set; }

        struct Sample { public float t, face, muzzle, distance; public bool steady; }
        class Case { public float d; public string where; public List<Sample> samples = new List<Sample>(); public Texture2D shot; public float firstSeen = -1f; }

        static readonly Stack<IEnumerator> Script = new Stack<IEnumerator>();
        static readonly List<Case> Cases = new List<Case>();
        static readonly List<GameObject> Built = new List<GameObject>();
        static int _lastFrame, _warm;
        static double _startedAt;
        static GameObject _player, _enemy;
        static PlayerMotor _motor;
        static Health _playerHealth;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;
        static string _error;

        public static void Batch() => Run();

        [MenuItem("FPSKit/Enemy AI/Measure Face Direction")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Script.Clear(); Cases.Clear(); Built.Clear();
            _lastFrame = -1; _warm = 0; _player = null; _error = null;
            _startedAt = EditorApplication.timeSinceStartup;
            Running = true;
            SaveMigration.Apply();
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("face test timed out");
                if (!EditorApplication.isPlaying || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tunedLevel == null)
                {
                    _tunedLevel = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }
                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null) Script.Push(All());
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                    if (_enemy == null || ai.gameObject != _enemy) ai.gameObject.SetActive(false);
                if (_playerHealth != null) _playerHealth.Heal(1000f);

                while (Script.Count > 0)
                {
                    var top = Script.Peek();
                    if (!top.MoveNext()) { Script.Pop(); continue; }
                    if (top.Current is IEnumerator sub) { Script.Push(sub); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
                Finish();
            }
        }

        static IEnumerator All()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            _motor = _player.GetComponent<PlayerMotor>();
            _playerHealth = _player.GetComponent<Health>();
            foreach (float d in new[] { 1f, 3f, 10f, 30f })
                foreach (string where in new[] { "left", "right", "above", "below" })
                    yield return One(d, where);
        }

        static IEnumerator One(float d, string where)
        {
            var c = new Case { d = d, where = where };
            Cases.Add(c);
            Clear();

            // The block: under the player when it is above, under the Grunt when it is below.
            float rise = (where == "above" || where == "below") ? d * Mathf.Tan(25f * Mathf.Deg2Rad) : 0f;
            float bearing = where == "left" ? -50f : where == "right" ? 50f : 0f;
            Vector3 enemyAt = new Vector3(0f, Floor, 0f);
            Vector3 playerAt = enemyAt + Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * d;

            var root = new GameObject("FaceTest_Root");
            Built.Add(root);
            Block(root, new Vector3(0f, Floor - 0.5f, 10f), new Vector3(90f, 1f, 90f));
            if (where == "above") { Block(root, playerAt + Vector3.up * (rise * 0.5f), new Vector3(2f, rise, 2f)); playerAt.y += rise; }
            if (where == "below") { Block(root, enemyAt + Vector3.up * (rise * 0.5f), new Vector3(4f, rise, 4f)); enemyAt.y += rise; }
            var surface = root.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.shadows = LightShadows.None;

            var cc = _player.GetComponent<CharacterController>();
            cc.enabled = false;
            _player.transform.position = playerAt + Vector3.up * 0.05f;
            cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++) yield return null;

            if (NavMesh.SamplePosition(enemyAt, out var hit, 1f, NavMesh.AllAreas)) enemyAt = hit.position;
            _enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab), enemyAt, Quaternion.identity);
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_enemy);
            var ai = _enemy.GetComponent<EnemyAI>();
            ai.attackDamage = 0f;
            ai.target = _player.transform;
            var brain = _enemy.GetComponent<EnemyBrain>();
            var body = _enemy.GetComponent<EnemyBody>();
            var anim = _enemy.GetComponentInChildren<Animator>();
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            RestAxes(anim, head, out Vector3 faceLocal, out Vector3 upLocal);
            var cam = Camera.main.transform;
            for (int i = 0; i < 5; i++) yield return null;
            if (brain != null) brain.HearCall(_player.transform.position, 0f);

            float start = Time.time;
            while (Time.time - start < CaseSeconds && _enemy != null)
            {
                _motor.TurnTowards(head.position, 1f);
                yield return null;
                if (body == null || head == null) break;
                // Alert, and the player in sight this last moment (read off the brain so the old build measures too).
                bool facesPlayer = brain != null && brain.WeaponUp && Time.time - Field(brain, "_lastSeen") < 1.5f;
                if (facesPlayer && c.firstSeen < 0f) c.firstSeen = Time.time;

                Vector3 face = head.rotation * faceLocal;
                Vector3 eye = head.position + (head.rotation * upLocal + face) * 0.09f * _enemy.transform.lossyScale.y;
                float faceErr = Vector3.Angle(face, cam.position - eye);
                float muzzleErr = float.NaN;
                if (body.gun != null && brain != null)
                    muzzleErr = Vector3.Angle(body.gun.forward, brain.LookPoint - body.gun.position);

                bool steady = facesPlayer && c.firstSeen >= 0f && Time.time - c.firstSeen > Settle
                              && Field(body, "_aimWeight") > 0.99f && Field(body, "_actionWeight") < 0.01f
                              && !body.IsDown && !ai.IsStaggered;
                c.samples.Add(new Sample { t = Time.time - start, face = faceErr, muzzle = muzzleErr, steady = steady,
                                           distance = Vector3.Distance(cam.position, eye) });
            }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && d <= 10f && head != null)
            {
                _motor.TurnTowards(head.position, 1f);
                yield return null;
                c.shot = Render(Camera.main);
            }
        }

        static float Field(object owner, string name)
            => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) is FieldInfo f ? (float)f.GetValue(owner) : float.NaN;

        /// <summary>The face's and the head's up direction in the head bone's own space, from the rest pose.</summary>
        static void RestAxes(Animator anim, Transform bone, out Vector3 face, out Vector3 up)
        {
            var rest = new Dictionary<string, Quaternion>();
            foreach (var b in anim.avatar.humanDescription.skeleton) rest[b.name] = b.rotation;
            Quaternion r = Quaternion.identity;
            for (var t = bone; t != null && t != anim.transform; t = t.parent)
                r = (rest.TryGetValue(t.name, out var local) ? local : t.localRotation) * r;
            face = Quaternion.Inverse(r) * Vector3.forward;
            up = Quaternion.Inverse(r) * Vector3.up;
        }

        static GameObject Block(GameObject root, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "FaceTest_Block";
            go.transform.SetParent(root.transform, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            return go;
        }

        static void Clear()
        {
            if (_enemy != null) Object.Destroy(_enemy);
            _enemy = null;
            foreach (var go in Built) if (go != null) Object.Destroy(go);
            Built.Clear();
        }

        static Texture2D Render(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        static float Percentile(List<float> v, float p)
        {
            if (v.Count == 0) return float.NaN;
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp(Mathf.RoundToInt(p * (s.Count - 1)), 0, s.Count - 1)];
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(Folder);
            var lines = new List<string>();
            bool ok = _error == null && Cases.Count == 16;
            foreach (var c in Cases)
            {
                var steady = c.samples.Where(s => s.steady).ToList();
                var face = steady.Select(s => s.face).ToList();
                var muzzle = steady.Where(s => !float.IsNaN(s.muzzle)).Select(s => s.muzzle).ToList();
                float med = Percentile(face, 0.5f);
                bool pass = face.Count >= 30 && med <= MaxMedian;
                ok &= pass;
                float turned = float.NaN;
                if (c.firstSeen >= 0f)
                    foreach (var s in c.samples) if (s.face < MaxMedian && s.t > 0f) { turned = s.t; break; }
                lines.Add($"  {c.d,4:0} m {c.where,-5}: face to camera median {med,5:0.0}  p90 {Percentile(face, 0.9f),5:0.0}  max {(face.Count > 0 ? face.Max() : float.NaN),5:0.0} deg"
                          + $" | muzzle off its aim point median {Percentile(muzzle, 0.5f),5:0.0} p90 {Percentile(muzzle, 0.9f),5:0.0}"
                          + $" | {steady.Count}/{c.samples.Count} frames steady, distance {(steady.Count > 0 ? steady.Min(s => s.distance) : 0f):0.0}-{(steady.Count > 0 ? steady.Max(s => s.distance) : 0f):0.0} m"
                          + $" | first under {MaxMedian:0} deg at {turned:0.00} s{(pass ? "" : "  <-- FAIL")}");
            }

            var shots = Cases.Where(c => c.shot != null).ToList();
            if (shots.Count > 0)
            {
                int cols = 4, rows = (shots.Count + cols - 1) / cols;
                var sheet = new Texture2D(W * cols, H * rows, TextureFormat.RGB24, false);
                for (int i = 0; i < shots.Count; i++)
                    sheet.SetPixels32((i % cols) * W, (rows - 1 - i / cols) * H, W, H, shots[i].shot.GetPixels32());
                sheet.Apply();
                File.WriteAllBytes($"{Folder}/faces.png", sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            foreach (var c in Cases) if (c.shot != null) Object.DestroyImmediate(c.shot);

            string report = (ok ? "face test passed" : "face test FAILED")
                            + $": angle between an aware enemy's face and the player's camera, 60 fps, after a {Settle:0.0} s turn (median allowed {MaxMedian:0} deg)"
                            + (_error != null ? "\n  error: " + _error : "")
                            + "\n" + string.Join("\n", lines)
                            + (shots.Count > 0 ? $"\n  pictures from the player's camera at the end of each case up to 10 m, rows 1, 3, 10 m, columns left right above below: {Folder}/faces.png" : "");
            File.WriteAllText($"{Folder}/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);

            Clear();
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null; _clockBackup = -1f;
            Time.captureDeltaTime = 0f;
            Running = false;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(ok ? 0 : 1);
        }
    }
}
#endif
