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
///
/// <b>Corners are slightly rounded</b> (the theme's <see cref="UITheme.cornerRadius"/>, 6 units),
/// by Ishaan's call on 2026-09-30 -- still flat and matte, just softer. A shape can ask for its
/// own radius, 0 for square (full-screen shades, the top bar, tabs), or a huge one for a pill
/// (a switch); the radius is clamped to half the short side either way. Round shapes are laid
/// as a fill fan and a border ring between two matching outlines, so fill and border still
/// never overlap, and a stripe follows the curve of its side's corners.
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

    [Header("Corners")]
    [Tooltip("Corner radius in canvas units. Negative means the theme's radius; 0 is square; " +
             "anything over half the short side makes a pill.")]
    [SerializeField] float _cornerRadius = -1f;

    float _builtScale = -1f;

    public float cornerRadius { get => _cornerRadius; set { if (Mathf.Approximately(_cornerRadius, value)) return; _cornerRadius = value; SetVerticesDirty(); } }

    float Radius(Rect r)
    {
        float want = _cornerRadius >= 0f ? _cornerRadius : UITheme.Active != null ? UITheme.Active.cornerRadius : 0f;
        return Mathf.Clamp(want, 0f, Mathf.Min(r.width, r.height) * 0.5f);
    }

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

        float radius = Radius(r);
        if (radius > 0.25f)
        {
            Rounded(vh, r, radius, left, right, top, bottom, s);
            return;
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

    const int CornerSteps = 6;

    /// <summary>
    /// A rounded rectangle: the outline walked counter-clockwise from the right edge, with
    /// the same number of points on the inner (fill) outline, so the border is a ring of
    /// quads between them and the fill is a fan inside. Each ring quad takes the colour of the
    /// side it is on, which is how a stripe sits along one side and bends into its corners.
    /// </summary>
    void Rounded(VertexHelper vh, Rect r, float radius, float left, float right, float top, float bottom, float stripe)
    {
        var outer = new System.Collections.Generic.List<Vector2>(4 * (CornerSteps + 1));
        var inner = new System.Collections.Generic.List<Vector2>(4 * (CornerSteps + 1));
        var sides = new System.Collections.Generic.List<Side>(4 * (CornerSteps + 1));

        // Corner centres, and the insets that meet at each corner.
        var corners = new[]
        {
            (c: new Vector2(r.xMax - radius, r.yMax - radius), from: 0f,   h: right, v: top,    a: Side.Right, b: Side.Top),
            (c: new Vector2(r.xMin + radius, r.yMax - radius), from: 90f,  h: left,  v: top,    a: Side.Top,   b: Side.Left),
            (c: new Vector2(r.xMin + radius, r.yMin + radius), from: 180f, h: left,  v: bottom, a: Side.Left,  b: Side.Bottom),
            (c: new Vector2(r.xMax - radius, r.yMin + radius), from: 270f, h: right, v: bottom, a: Side.Bottom, b: Side.Right),
        };
        foreach (var k in corners)
        {
            for (int i = 0; i <= CornerSteps; i++)
            {
                float t = i / (float)CornerSteps;
                float ang = (k.from + 90f * t) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                outer.Add(k.c + dir * radius);
                // The inner outline is the outer one pulled in by each side's width: an
                // ellipse where the two widths differ, so a thick stripe stays even.
                inner.Add(k.c + new Vector2(dir.x * Mathf.Max(0f, radius - k.h), dir.y * Mathf.Max(0f, radius - k.v)));
                sides.Add(t < 0.5f ? k.a : k.b);
            }
        }

        // Fill: a fan from the centre over the inner outline. Emitted even when clear, for
        // the raycast reason the square path gives.
        Color32 fill = color;
        int centre = vh.currentVertCount;
        vh.AddVert(new Vector3(r.center.x, r.center.y), fill, Vector2.zero);
        for (int i = 0; i < inner.Count; i++) vh.AddVert(inner[i], fill, Vector2.zero);
        for (int i = 0; i < inner.Count; i++)
            vh.AddTriangle(centre, centre + 1 + i, centre + 1 + (i + 1) % inner.Count);

        // Border ring, a quad per segment, coloured by side.
        for (int i = 0; i < outer.Count; i++)
        {
            int j = (i + 1) % outer.Count;
            var side = sides[i];
            Color c = side == _stripeSide && stripe > 0f ? _stripeColor : _borderColor;
            if (c.a <= 0f) continue;
            if ((inner[i] - outer[i]).sqrMagnitude < 1e-6f && (inner[j] - outer[j]).sqrMagnitude < 1e-6f) continue;
            Color32 c32 = c;
            int v = vh.currentVertCount;
            vh.AddVert(outer[i], c32, Vector2.zero);
            vh.AddVert(outer[j], c32, Vector2.zero);
            vh.AddVert(inner[j], c32, Vector2.zero);
            vh.AddVert(inner[i], c32, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v + 2, v + 3, v);
        }
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
