using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Lays the right-thumb button cluster out in millimetres, on the device it is actually
/// running on.
///
/// <b>The builder cannot do this, and that is the whole reason this exists.</b> A scene
/// is authored against a 1920x1080 reference canvas, so a button placed there is placed
/// in reference units -- and a reference unit is not a distance. The cluster shipped
/// with its buttons 35 to 60 reference units apart, which sounds like clearance and is
/// 2.4mm on every phone measured. <see cref="TouchMetrics"/> puts a thumb at about 20mm
/// across. Two buttons 2.4mm apart under a 20mm thumb are not two buttons; they are one
/// button that does an unpredictable thing, and from the outside it reads as the game
/// registering the wrong input rather than as a layout that is too tight.
///
/// It was invisible to every check because nothing actually overlapped. <c>VerifyTouch</c>
/// asked whether two rects intersect, which is a question about zero clearance, when the
/// question a thumb asks is whether there is a thumb's width between them.
///
/// So the arrangement is authored here as a grid of logical slots -- which column, which
/// row, is it the primary -- and the real geometry is worked out at <c>Start</c> from
/// <see cref="TouchMetrics.Dpi"/> and the canvas scale. The same layout is then the same
/// physical size on a 270dpi budget phone and a 416dpi flagship, which is the entire
/// point of measuring a control in millimetres.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TouchCluster : MonoBehaviour
{
    /// <summary>One button's place in the arrangement.</summary>
    [System.Serializable]
    public class Slot
    {
        public TouchButton button;

        [Tooltip("Columns count leftwards from the right edge; the nearest column is 0 " +
                 "because that is where the thumb rests.")]
        [Min(0)] public int column;

        [Tooltip("Rows count upwards from the bottom edge, for the same reason.")]
        [Min(0)] public int row;

        [Tooltip("The one button the thumb must find without looking. Drawn larger.")]
        public bool primary;

        [Tooltip("Shown only while the player is carrying the equipment it uses. A " +
                 "button for something you do not have is worse than no button: it is " +
                 "pressed, nothing happens, and the rest of the controls stop being " +
                 "trusted. Same rule the HUD's key strip already follows.")]
        public bool situational;
    }

    [Header("Wiring")]
    public TouchProfile profile;

    public List<Slot> slots = new List<Slot>();

    [Header("Sizes, in millimetres on the real screen")]
    [Tooltip("The primary button -- fire. Generous on purpose: it is held down for most " +
             "of a fight while the same thumb is dragging to aim.")]
    [Range(9f, 22f)] public float primaryMm = 15f;

    [Tooltip("Everything else in the cluster.")]
    [Range(8f, 18f)] public float secondaryMm = 11.5f;

    [Tooltip("Clear space between one button's edge and the next. This is the number " +
             "that was effectively zero before. A thumb is about 20mm wide, so this " +
             "does not have to be a whole thumb -- the centres do the work -- but it " +
             "has to be enough that the contact patch has somewhere to land.")]
    [Range(2f, 12f)] public float gapMm = 5f;

    [Tooltip("From the edges of the safe area. Phones round their corners and put " +
             "gesture bars along the bottom.")]
    [Range(2f, 16f)] public float marginMm = 6f;

    RectTransform _rect;

    void Awake() => _rect = (RectTransform)transform;

    void Start() => Rebuild();

    /// <summary>
    /// Re-measures and re-places every button. Safe to call again -- it derives
    /// everything from the slot list and the screen, and keeps nothing.
    /// </summary>
    public void Rebuild()
    {
        if (_rect == null) _rect = (RectTransform)transform;

        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (scale <= 0.0001f) scale = 1f;

        // Millimetres to the units this canvas is authored in.
        float unit = TouchMetrics.MillimetresToPixels(1f) / scale;

        float floorMm = profile != null ? profile.minButtonMm : 9f;
        float primary = Mathf.Max(primaryMm, floorMm) * unit;
        float secondary = Mathf.Max(secondaryMm, floorMm) * unit;
        float gap = gapMm * unit;
        float margin = marginMm * unit;

        var live = new List<Slot>();
        foreach (var slot in slots)
        {
            if (slot == null || slot.button == null) continue;

            bool show = !slot.situational || Carrying(slot.button.action);
            slot.button.gameObject.SetActive(show);

            if (show) live.Add(slot);
        }

        // Column widths and row heights come from what is actually on screen, so a
        // situational button that is not being carried collapses its column rather than
        // leaving a hole the thumb has to travel across.
        var colWidth = new Dictionary<int, float>();
        var rowHeight = new Dictionary<int, float>();

        foreach (var slot in live)
        {
            float size = slot.primary ? primary : secondary;

            colWidth[slot.column] = Mathf.Max(Get(colWidth, slot.column), size);
            rowHeight[slot.row] = Mathf.Max(Get(rowHeight, slot.row), size);
        }

        foreach (var slot in live)
        {
            float size = slot.primary ? primary : secondary;

            var rect = (RectTransform)slot.button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            float x = margin + Get(colWidth, slot.column) * 0.5f;
            for (int c = 0; c < slot.column; c++) x += Get(colWidth, c) + gap;

            float y = margin + Get(rowHeight, slot.row) * 0.5f;
            for (int r = 0; r < slot.row; r++) y += Get(rowHeight, r) + gap;

            rect.anchoredPosition = new Vector2(-x, y);

            Dress(slot.button, size);
            }
        }

    /// <summary>
    /// Scales the parts inside a button to the size the button just became.
    ///
    /// The ring, the padding and the type all have to be fractions of the button rather
    /// than fixed units, because the button's size is decided here and only here. Left
    /// absolute, a seven-unit ring is a hairline on a dense screen and a thick band on a
    /// coarse one -- the two things that make a control look unconsidered, from the same
    /// cause.
    /// </summary>
    static void Dress(TouchButton button, float size)
    {
        // The fill, inset to leave the ring showing.
        if (button.target != null)
        {
            var fill = button.target.rectTransform;
            float inset = Mathf.Max(2f, size * 0.055f);

            fill.offsetMin = new Vector2(inset, inset);
            fill.offsetMax = new Vector2(-inset, -inset);
        }

        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;

        float pad = size * 0.16f;
        var labelRect = label.rectTransform;
        labelRect.offsetMin = new Vector2(pad, pad);
        labelRect.offsetMax = new Vector2(-pad, -pad);

        // Auto-sizing still picks the final number -- "RELOAD" and "II" cannot share one
        // -- but the range it picks from is proportional, so the type is the same
        // fraction of the button on every screen.
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.11f;
        label.fontSizeMax = size * 0.30f;
    }

    static float Get(Dictionary<int, float> map, int key)
        => map.TryGetValue(key, out float value) ? value : 0f;

    /// <summary>
    /// Whether the player has the equipment a situational button drives. Asked of the
    /// live rig rather than of the store, because what matters is what is in their hands
    /// in this level.
    /// </summary>
    static bool Carrying(TouchButton.ActionKind action)
    {
        switch (action)
        {
            case TouchButton.ActionKind.Bomb:
            {
                var thrower = FindAnyObjectByType<BombThrower>();
                return thrower != null && thrower.data != null;
            }

            case TouchButton.ActionKind.UseItem:
            {
                var belt = FindAnyObjectByType<ConsumableBelt>();
                return belt != null && belt.data != null && belt.Count > 0;
            }

            default:
                return true;
        }
    }
}
