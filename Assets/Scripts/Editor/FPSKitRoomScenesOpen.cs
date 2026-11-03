#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Opening the desert in the editor opens its district room scenes with it (FPSKitTownRoomScenes), so the town is seen
    /// with its insides, as in play. Not in batch mode: the batch tools open the town alone and take what they need.
    /// </summary>
    [InitializeOnLoad]
    static class FPSKitRoomScenesOpen
    {
        static FPSKitRoomScenesOpen()
        {
            EditorSceneManager.sceneOpened -= Opened;
            EditorSceneManager.sceneOpened += Opened;
        }

        static void Opened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            if (Application.isBatchMode || mode != OpenSceneMode.Single || scene.path != FPSKitTownGI.DesertScene) return;
            foreach (var path in FPSKitTownRoomScenes.ExistingPaths())
                EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }
    }
}
#endif
