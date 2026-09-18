using UnityEngine;

/// <summary>
/// Pulls a RectTransform inside the screen's safe area, so nothing the player needs
/// lands under a notch, a punch-hole camera, a rounded corner or the home gesture bar.
///
/// Phones stopped being rectangles years ago and Unity does not account for it: a canvas
/// fills the whole panel, cutout included. What that costs is specific and bad. The
/// pause button is anchored to the top-right corner, which on most modern phones held in
/// landscape is exactly where the front camera is -- so it is drawn underneath the
/// cutout, where roughly half of it cannot be seen and, on some devices, cannot be
/// touched either. It is also the only way out of a level: a phone has no Escape key, so
/// a player who cannot press it cannot pause, cannot reach the dashboard and cannot leave
/// the run except by killing the app.
///
/// The bottom edge is the same story with the gesture bar. A FIRE button sitting in the
/// bottom-right corner of a gesture-navigation phone competes with the swipe that closes
/// the game, and losing that race means the player is dropped to the home screen in the
/// middle of a firefight.
///
/// <b>Re-applied when the area changes, not once at startup.</b> Rotating the device,
/// folding a foldable, or entering multi-window all move it, and a fitter that ran once
/// in Awake leaves the controls laid out for the shape the phone used to be.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("Inset the left and right edges. On in landscape, which is the orientation " +
             "this game is played in: a cutout is on a long edge, and that edge is the " +
             "side of the screen when the phone is turned.")]
    public bool fitHorizontally = true;

    [Tooltip("Inset the top and bottom edges. Keeps controls clear of the gesture bar.")]
    public bool fitVertically = true;

    [Tooltip("Extra breathing room beyond the safe area, in millimetres. The reported " +
             "area is the minimum that is not physically obscured, which is not the same " +
             "as somewhere comfortable to put a button you press under pressure.")]
    [Range(0f, 6f)] public float paddingMm = 1.5f;

    RectTransform _rect;
    Rect _applied;
    Vector2Int _appliedResolution;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        Apply();
    }

    void OnEnable() => Apply();

    void Update()
    {
        // Cheap: two struct comparisons against values Unity already has. A rotation or
        // a fold is rare, and noticing it late is a frame of controls in the wrong place.
        if (Screen.safeArea != _applied ||
            Screen.width != _appliedResolution.x || Screen.height != _appliedResolution.y)
            Apply();
    }

    void Apply()
    {
        if (_rect == null) _rect = GetComponent<RectTransform>();
        if (_rect == null || Screen.width <= 0 || Screen.height <= 0) return;

        Rect area = Screen.safeArea;

        _applied = area;
        _appliedResolution = new Vector2Int(Screen.width, Screen.height);

        float pad = TouchMetrics.MillimetresToPixels(paddingMm);

        float xMin = fitHorizontally ? area.xMin + pad : 0f;
        float xMax = fitHorizontally ? area.xMax - pad : Screen.width;
        float yMin = fitVertically ? area.yMin + pad : 0f;
        float yMax = fitVertically ? area.yMax - pad : Screen.height;

        // A safe area that has eaten the whole screen is a platform reporting nonsense,
        // and obeying it would leave a control layer with no area at all -- which looks
        // exactly like the controls failing to build.
        if (xMax - xMin < Screen.width * 0.5f || yMax - yMin < Screen.height * 0.5f)
        {
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            return;
        }

        _rect.anchorMin = new Vector2(xMin / Screen.width, yMin / Screen.height);
        _rect.anchorMax = new Vector2(xMax / Screen.width, yMax / Screen.height);

        // Anchors carry the inset, so the offsets must be zero or they would add to it.
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;
    }
}
