using UnityEngine;

/// <summary>
/// Lives in a reduced arena scene (see FPSKitReducedCopy in the editor tools) and makes it cheap to run on a
/// laptop that overheats: it caps the frame rate and drops the texture resolution while the scene is playing, and
/// gives both back when it stops. The scene itself has no shadow casters, few lights and thinned scenery.
///
/// It is part of the shipped Night Citylife and Abandoned Fairground scenes; rebuilding either arena with the scene builder produces the full-quality scene without it.
/// </summary>
public class ArenaPerformance : MonoBehaviour
{
    [Tooltip("Frame cap while this scene plays. Heat is roughly proportional to frames drawn, so 30 is about half the load of 60.")]
    [Range(15, 60)] public int targetFrameRate = 30;

    [Tooltip("How many mip levels to drop from every texture: 1 is half resolution, 2 is a quarter (a sixteenth of the memory).")]
    [Range(0, 3)] public int textureMipLimit = 2;

    // What the player's own settings were before this scene took over. Not serialized.
    int _savedFps, _savedVSync, _savedMips;
    bool _saved;

    void OnEnable()
    {
        if (!_saved)
        {
            _savedFps = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
            _savedMips = QualitySettings.globalTextureMipmapLimit;
            _saved = true;
        }

        QualitySettings.vSyncCount = 0;                       // the cap below only applies with vSync off
        Application.targetFrameRate = targetFrameRate;
        QualitySettings.globalTextureMipmapLimit = textureMipLimit;
    }

    void OnDisable()
    {
        if (!_saved) return;
        Application.targetFrameRate = _savedFps;
        QualitySettings.vSyncCount = _savedVSync;
        QualitySettings.globalTextureMipmapLimit = _savedMips;
        _saved = false;
    }
}
