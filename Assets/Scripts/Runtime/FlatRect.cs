using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A solid rectangle with a border and an optional stripe down one side: the one shape
/// every panel, button, bar and card in the interface is drawn with.
///
/// <b>Lines are measured in screen pixels, not canvas units.</b> The canvas scales with the
/// window, so a border authored as one unit is 0.67 of a pixel at 1280x720 and 1.33 at
/// 2560x1440 -- smeared at one size and heavy at the other, and never the crisp single line
/// the style asks for. Here the widths are divided by the canvas's scale factor when the
/// mesh is built, and rebuilt whenever that factor moves.
///
/// <b>Fill, border and stripe never overlap.</b> They are laid as separate quads that meet
/// edge to edge, so a HUD panel at 85% opacity does not show a darker line where the
/// border is drawn over its own fill.
///
/// No sprite, no texture, no nine-slicing: the colour on the Graphic is the fill, and
/// everything else is a field here.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class FlatRect : MaskableGraphic
{
    public enum Side { None, Left, Top, Right, Bottom }

    [Header("Border")]
    [Tooltip("The border colour. Alpha zero for no border.")]
    [SerializeField] Color _borderColor = new Color(0, 0, 0, 0);

    [Tooltip("Border thickness in screen pixels.")]
    [SerializeField, Min(0f)] float _borderPixels = 1f;

    [Header("Stripe")]
    [Tooltip("A thicker band along one edge: an arena's colour on its card, the selection " +
             "line under a tab, the state colour down the side of a toast.")]
    [SerializeField] Side _stripeSide = Side.None;

    [SerializeField] Color _stripeColor = new Color(0, 0, 0, 0);

    [Tooltip("Stripe thickness in screen pixels.")]
    [SerializeField, Min(0f)] float _stripePixels = 3f;

    float _builtScale = -1f;

    protected override void OnEnable()
    {
        base.OnEnable();
        // Same reason the clear fill is still emitted: a culled mesh is not a raycast target.
        canvasRenderer.cullTransparentMesh = false;
    }

    public Color borderColor { get => _borderColor; set { if (_borderColor == value) return; _borderColor = value; SetVerticesDirty(); } }
    public float borderPixels { get => _borderPixels; set { if (Mathf.Approximately(_borderPixels, value)) return; _borderPixels = value; SetVerticesDirty(); } }
    public Side stripeSide { get => _stripeSide; set { if (_stripeSide == value) return; _stripeSide = value; SetVerticesDirty(); } }
    public Color stripeColor { get => _stripeColor; set { if (_stripeColor == value) return; _stripeColor = value; SetVerticesDirty(); } }
    public float stripePixels { get => _stripePixels; set { if (Mathf.Approximately(_stripePixels, value)) return; _stripePixels = value; SetVerticesDirty(); } }

    /// <summary>Canvas units per screen pixel, so a width in pixels can be laid in canvas units.</summary>
    float UnitsPerPixel
    {
        get
        {
            var c = canvas;
            float scale = c != null ? c.scaleFactor : 1f;
            return scale > 0.0001f ? 1f / scale : 1f;
        }
    }

    /// <summary>
    /// A CanvasScaler changes the factor without telling its graphics, so a border built at
    /// one window size would keep that thickness at every other. Checked once a frame; the
    /// rebuild only happens when it actually moved.
    /// </summary>
    void LateUpdate()
    {
        var c = canvas;
        if (c != null && !Mathf.Approximately(c.scaleFactor, _builtScale)) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var c = canvas;
        _builtScale = c != null ? c.scaleFactor : 1f;

        Rect r = GetPixelAdjustedRect();
        float u = UnitsPerPixel;
        float b = _borderColor.a > 0f ? Mathf.Min(_borderPixels * u, Mathf.Min(r.width, r.height) * 0.5f) : 0f;
        float s = _stripeSide != Side.None && _stripeColor.a > 0f ? _stripePixels * u : 0f;

        // The stripe replaces the border on its own side rather than sitting inside it,
        // so the two never double up on one edge.
        float left = b, right = b, top = b, bottom = b;
        switch (_stripeSide)
        {
            case Side.Left: left = Mathf.Max(s, 0f); break;
            case Side.Right: right = Mathf.Max(s, 0f); break;
            case Side.Top: top = Mathf.Max(s, 0f); break;
            case Side.Bottom: bottom = Mathf.Max(s, 0f); break;
        }
        if (s <= 0f)
        {
            left = right = top = bottom = b;
        }

        float x0 = r.xMin, x1 = r.xMax, y0 = r.yMin, y1 = r.yMax;
        float ix0 = x0 + left, ix1 = x1 - right, iy0 = y0 + bottom, iy1 = y1 - top;

        // Fill. Emitted even when fully transparent: a quiet button or a tab has a clear
        // face and is still the raycast target, and a graphic with no mesh gets no depth
        // from the canvas -- which the raycaster reads as "not drawn" and skips.
        if (ix1 > ix0 && iy1 > iy0)
            Quad(vh, ix0, iy0, ix1, iy1, color, always: true);

        // Edges. Top and bottom run the full width; left and right fill the gap between
        // them, so corners are covered exactly once.
        Color Edge(Side side) => side == _stripeSide && s > 0f ? _stripeColor : _borderColor;
        if (top > 0f) Quad(vh, x0, iy1, x1, y1, Edge(Side.Top));
        if (bottom > 0f) Quad(vh, x0, y0, x1, iy0, Edge(Side.Bottom));
        if (left > 0f) Quad(vh, x0, iy0, ix0, iy1, Edge(Side.Left));
        if (right > 0f) Quad(vh, ix1, iy0, x1, iy1, Edge(Side.Right));
    }

    static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c, bool always = false)
    {
        if (c.a <= 0f && !always) return;
        Color32 c32 = c;
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(x0, y0), c32, Vector2.zero);
        vh.AddVert(new Vector3(x0, y1), c32, Vector2.zero);
        vh.AddVert(new Vector3(x1, y1), c32, Vector2.zero);
        vh.AddVert(new Vector3(x1, y0), c32, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }
}
