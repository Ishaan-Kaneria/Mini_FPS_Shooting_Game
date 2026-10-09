#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The AI test scene (Docs/ENEMY_AI_PLAN.md): a flat yard with low and high cover, a
    /// baked navmesh and a target standing in for the player, for the enemy AI's scripted
    /// tests (the limb test first; the squad scenarios later). Generated: rebuild it with
    /// FPSKit > Enemy AI > Build AI Test Scene rather than editing it.
    /// </summary>
    public static class FPSKitAITestScene
    {
        public const string ScenePath = "Assets/FPSKit_Generated/Scenes/AITest.unity";
        const string NavMeshPath = "Assets/FPSKit_Generated/Scenes/AITest_NavMesh.asset";

        [MenuItem("FPSKit/Enemy AI/Build AI Test Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Makes the scene and leaves it open.</summary>
        public static void Build()
        {
            FPSKitSceneBuilder.EnsureProjectTagsAndLayers();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            int environment = LayerMask.NameToLayer("Environment");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.layer = environment;
            floor.isStatic = true;

            // Cover: waist-high crates and head-high walls, enough to hide behind and lean out of.
            void Block(string name, Vector3 at, Vector3 size)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.position = at + Vector3.up * size.y * 0.5f;
                b.transform.localScale = size;
                b.layer = environment;
                b.isStatic = true;
            }
            Block("LowCover_A", new Vector3(-8f, 0f, 20f), new Vector3(2.4f, 1.0f, 0.8f));
            Block("LowCover_B", new Vector3(7f, 0f, 22f), new Vector3(2.4f, 1.0f, 0.8f));
            Block("HighCover_A", new Vector3(-3f, 0f, 27f), new Vector3(3f, 2.2f, 0.5f));
            Block("HighCover_B", new Vector3(12f, 0f, 16f), new Vector3(0.5f, 2.2f, 3f));
            Block("LowCover_C", new Vector3(-14f, 0f, 12f), new Vector3(0.8f, 1.0f, 2.4f));

            var surface = floor.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
            if (AssetDatabase.LoadAssetAtPath<Object>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);

            // The player, as far as the enemies are concerned: something tagged Player with
            // a lot of health to shoot at.
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);
            var health = player.AddComponent<Health>();
            health.maxHealth = 1000000f;

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 4f, -6f);
                cam.transform.LookAt(new Vector3(0f, 1f, 14f));
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[FPSKit] AI test scene built: " + ScenePath);
        }
    }
}
#endif
