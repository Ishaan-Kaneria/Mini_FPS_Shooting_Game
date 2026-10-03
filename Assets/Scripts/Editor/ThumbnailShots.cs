#if UNITY_EDITOR
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// One arena's thumbnail cameras: the arena card shot, and an optional shot per level.
    /// Lives beside the images in Assets/UI/Thumbnails/&lt;Arena&gt;/Shots.asset. It is hand-kept,
    /// not generated, so a scene rebuild never moves a camera.
    /// </summary>
    public class ThumbnailShots : ScriptableObject
    {
        [System.Serializable]
        public struct View
        {
            [Tooltip("Where the camera stands, in world space.")]
            public Vector3 position;
            [Tooltip("The point it looks at, in world space.")]
            public Vector3 lookAt;
            [Tooltip("Vertical field of view in degrees.")]
            [Range(20f, 100f)] public float fov;
            [Tooltip("Hours of the day are not simulated; this scales the main light so a dusk arena is not black on the card. 1 = as built.")]
            [Range(0.5f, 3f)] public float exposure;
        }

        [Header("Arena card")]
        [Tooltip("The picture on the arena card, the Current Mission card and the level select.")]
        public View arena = new View { fov = 70f, exposure = 1f };

        [Header("Per level")]
        [Tooltip("Optional. Entry n is level n+1 of the arena; a level without one uses the arena shot.")]
        public View[] levels = new View[0];
    }
}
#endif
