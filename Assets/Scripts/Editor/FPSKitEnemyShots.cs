#if UNITY_EDITOR
using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Photographs the enemy doing what it does: walking, limping, crawling, shooting,
    /// hanging a wrecked arm, and dying four different ways, with the blood that leaves.
    ///
    /// The enemy is procedural and animated by code, so nothing in the prefab says what
    /// it looks like in motion -- the only way to check a change to the body or the
    /// animator is to watch it. This stands real prefabs with real archetypes on a lit
    /// floor with a navmesh, gives them somebody to hunt, wounds some of them through
    /// their real hitboxes, and renders a camera at fixed moments.
    ///
    /// Needs a graphics device:
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureEnemies -fpskitOut Build/Enemies
    ///
    /// File names are fixed, so an aborted run leaves the last run's pictures behind:
    /// read the exit code, not the folder.
    /// </summary>
    public static class FPSKitEnemyShots
    {
        const string PrefabPath = "Assets/FPSKit_Generated/Enemy.prefab";
        const int Width = 1280, Height = 720;

        static string _folder;
        static double _start;
        static int _step;
        static Transform _player;
        static GameObject[] _enemies;
        static bool _bloodBackup;

        public static void Capture(string folder)
        {
            _folder = string.IsNullOrEmpty(folder) ? "Build/Enemies" : folder;
            Directory.CreateDirectory(_folder);

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(40f, -40f, 0f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.layer = LayerMask.NameToLayer("Environment");
            var concrete = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/FPSKit_Generated/Materials/IndustrialWarehouse_Floor_Tiled.mat");
            if (concrete != null) floor.GetComponent<Renderer>().sharedMaterial = concrete;

            // A wall behind the line for the blood to reach.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = new Vector3(0f, 2f, 26f);
            wall.transform.localScale = new Vector3(30f, 4f, 0.5f);
            wall.layer = floor.layer;
            if (concrete != null) wall.GetComponent<Renderer>().sharedMaterial = concrete;

            _bloodBackup = GameSettings.Blood;
            GameSettings.Blood = true;

            _step = 0;
            _start = EditorApplication.timeSinceStartup;

            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
        }

        static double Elapsed => EditorApplication.timeSinceStartup - _start;

        static void Tick()
        {
            try
            {
                if (Elapsed > 120) throw new Exception("enemy capture timed out");

                switch (_step)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        SaveMigration.Apply();
                        Setup();
                        _start = EditorApplication.timeSinceStartup;
                        _step = 1;
                        return;

                    case 1:
                        if (Elapsed < 1.2) return;
                        Shoot("01_standing", Close(_enemies[0], 3.6f), At(_enemies[0]), 38f);
                        _step = 2;
                        return;

                    case 2:
                        if (Elapsed < 2.4) return;
                        Shoot("02_advancing", new Vector3(9f, 1.7f, 4f), new Vector3(0f, 1f, 14f), 45f);
                        _step = 3;
                        return;

                    case 3:
                        if (Elapsed < 3.2) return;
                        Shoot("03_wounded_closeup", Close(_enemies[1], 2.4f), At(_enemies[1]), 35f);
                        Shoot("04_crawling", Close(_enemies[2], 3.2f, 0.9f), At(_enemies[2], 0.4f), 40f);
                        Shoot("05_disarmed", Close(_enemies[4], 2.8f), At(_enemies[4]), 38f);
                        _step = 4;
                        return;

                    case 4:
                        if (Elapsed < 5.5) return;
                        Shoot("06_firing", new Vector3(-6f, 1.6f, 2f), new Vector3(0f, 1.2f, 12f), 45f);
                        Kill();
                        _step = 5;
                        return;

                    case 5:
                        if (Elapsed < 5.85) return;
                        Shoot("07_dying", new Vector3(10f, 2f, 3f), new Vector3(0f, 0.8f, 13f), 50f);
                        _step = 6;
                        return;

                    case 6:
                        if (Elapsed < 9.5) return;
                        Shoot("08_aftermath", new Vector3(8f, 4f, 2f), new Vector3(0f, 0f, 13f), 50f);
                        Shoot("09_aftermath_close", new Vector3(3f, 2.2f, 7.5f), new Vector3(0f, 0.1f, 12f), 45f);
                        Done(0);
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                Done(1);
            }
        }

        static void Setup()
        {
            var floor = GameObject.Find("Floor");
            var surface = floor.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();

            // Somebody to hunt: a tagged capsule at the near end. No Health, so the shots
            // land on nothing and nobody wins.
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);
            player.GetComponent<Renderer>().enabled = false;
            _player = player.transform;

            _enemies = new[]
            {
                Spawn("Grunt", new Vector3(-4f, 0f, 18f)),     // sound
                Spawn("Grunt", new Vector3(-1.3f, 0f, 16f)),   // limping, bloodied
                Spawn("Runner", new Vector3(1.4f, 0f, 16f)),   // on the floor
                Spawn("Brute", new Vector3(4.2f, 0f, 19f)),    // creature
                Spawn("Marksman", new Vector3(-6.5f, 0f, 14f)),// disarmed
                Spawn("Grunt", new Vector3(6.5f, 0f, 15f)),    // spare, for a blast
            };

            Hit(_enemies[1], BodyPart.LeftLeg, 20f);
            Hit(_enemies[1], BodyPart.Torso, 18f);
            Hit(_enemies[1], BodyPart.RightArm, 8f);

            Hit(_enemies[2], BodyPart.RightLeg, 22f);
            Hit(_enemies[2], BodyPart.LeftLeg, 22f);

            for (int i = 0; i < 3; i++) Hit(_enemies[4], BodyPart.RightArm, 14f);
        }

        static void Kill()
        {
            Hit(_enemies[0], BodyPart.Head, 1f);
            Hit(_enemies[1], BodyPart.Torso, 200f);

            var blasted = _enemies[5].GetComponent<Health>();
            if (blasted != null)
                blasted.ApplyDamage(new DamageInfo(500f, _enemies[5].transform.position + Vector3.up,
                                                   Vector3.back, (Vector3.forward + Vector3.right * 0.4f).normalized, null)
                {
                    fromBlast = true,
                });

            Hit(_enemies[2], BodyPart.Torso, 200f);
        }

        static GameObject Spawn(string archetype, Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var enemy = Object.Instantiate(prefab, at, Quaternion.LookRotation(-at.normalized));
            enemy.name = archetype;
            FPSKitEnemyRoster.GetOrCreate(archetype).ApplyTo(enemy);

            var ai = enemy.GetComponent<EnemyAI>();
            if (ai != null) ai.target = _player;

            return enemy;
        }

        static void Hit(GameObject enemy, BodyPart part, float amount)
        {
            if (enemy == null) return;

            foreach (var hitbox in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (hitbox.Part != part) continue;

                var collider = hitbox.GetComponent<Collider>();
                Vector3 toward = (_player.position - enemy.transform.position).normalized;
                Vector3 point = collider.bounds.center + toward * collider.bounds.extents.z;
                hitbox.Receive(new DamageInfo(amount / Mathf.Max(0.01f, hitbox.damageMultiplier), point,
                                              toward, -toward, null));
                return;
            }
        }

        static Vector3 At(GameObject enemy, float height = 1.1f)
            => enemy != null ? enemy.transform.position + Vector3.up * height : Vector3.zero;

        static Vector3 Close(GameObject enemy, float distance, float height = 1.4f)
        {
            if (enemy == null) return Vector3.zero;

            Vector3 front = enemy.transform.forward + enemy.transform.right * 0.6f;
            return enemy.transform.position + front.normalized * distance + Vector3.up * height;
        }

        static void Shoot(string name, Vector3 from, Vector3 look, float fov)
        {
            var rig = new GameObject("ShotCamera");
            var camera = rig.AddComponent<Camera>();
            RenderTexture target = null;

            try
            {
                rig.transform.position = from;
                rig.transform.rotation = Quaternion.LookRotation((look - from).normalized, Vector3.up);
                camera.fieldOfView = fov;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 500f;
                camera.clearFlags = CameraClearFlags.Skybox;

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                var request = new RenderPipeline.StandardRequest { destination = target };

                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes($"{_folder}/{name}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Object.DestroyImmediate(rig);
            }
        }

        static void Done(int code)
        {
            GameSettings.Blood = _bloodBackup;
            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;

            if (code == 0) Debug.Log($"[FPSKitBatch] enemy shots written to {_folder}");
            EditorApplication.Exit(code);
        }
    }
}
#endif
