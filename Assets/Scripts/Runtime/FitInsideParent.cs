using UnityEngine;

/// <summary>
/// Scales a rect down, never up, until it fits inside its parent with a margin.
///
/// For a dialog whose height is its content: authored against a 1080-unit canvas it fits a
/// monitor with room to spare, and on a landscape phone the same content runs past the
/// bottom edge and takes the buttons with it. Shrinking the whole card keeps every line
/// and every button, at the one cost that is acceptable on a screen that is read and then
/// dismissed. Measured in LateUpdate, after layout has sized the content this frame.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FitInsideParent : MonoBehaviour
{
    [Tooltip("Canvas units kept clear on every side.")]
    [Min(0f)] public float margin = 24f;

    void LateUpdate()
    {
        var rt = (RectTransform)transform;
        if (!(rt.parent is RectTransform parent)) return;
        var size = rt.rect.size;
        var room = parent.rect.size - Vector2.one * (margin * 2f);
        if (size.x <= 1f || size.y <= 1f || room.x <= 1f || room.y <= 1f) return;
        float s = Mathf.Min(1f, room.x / size.x, room.y / size.y);
        if (Mathf.Abs(rt.localScale.x - s) > 0.001f) rt.localScale = new Vector3(s, s, 1f);
    }
}
