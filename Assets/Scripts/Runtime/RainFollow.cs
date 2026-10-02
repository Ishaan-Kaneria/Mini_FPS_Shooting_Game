using UnityEngine;

/// <summary>
/// Keeps the rain over the player: a particle box that follows the camera sideways, so the arena needs a
/// few thousand particles around the viewer and not a roof of them across 180 m.
///
/// Position only. The particle system itself is configured in the scene; to switch rain off (the Low quality
/// tier) disable this object, nothing else depends on it.
/// </summary>
public class RainFollow : MonoBehaviour
{
    [Header("Follow")]
    [Tooltip("How far above the camera the rain starts falling.")]
    [Min(2f)] public float height = 14f;

    Transform _camera;

    void LateUpdate()
    {
        if (_camera == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _camera = cam.transform;
        }
        var p = _camera.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
    }
}
