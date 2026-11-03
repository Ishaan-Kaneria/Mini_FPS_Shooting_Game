using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads the town's rooms with it: the desert's house and shop insides and their furniture live in district scenes of their
/// own (FPSKitTownRoomScenes), so each district can be baked alone and every room lit by its own lightmap (2026-11-03: one
/// bake of every room did not fit the laptop's memory). Sits in the town's scene; however the town is opened -- the menu, a
/// restart, a probe, a staged phone or web copy -- its rooms come with it. Loaded additively at once, so they are there
/// before the level's briefing ends; they go when the town goes (a single-scene load unloads them). The town stays the
/// active scene, so its sky, fog and light are the ones in use. Holds no static state.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public class RoomScenes : MonoBehaviour
{
    [Tooltip("The district scenes to load with this one, by name (they must be in the build's scene list).")]
    public string[] sceneNames = new string[0];

    void Awake()
    {
        foreach (var name in sceneNames)
        {
            if (string.IsNullOrEmpty(name) || SceneManager.GetSceneByName(name).isLoaded) continue;
            if (!Application.CanStreamedLevelBeLoaded(name))
            {
                Debug.LogWarning($"[RoomScenes] {name} is not in the build: its rooms are missing.");
                continue;
            }
            SceneManager.LoadScene(name, LoadSceneMode.Additive);
        }
    }
}
