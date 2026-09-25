using UnityEngine;

/// <summary>
/// One element the player can move, resize, fade or hide in the HUD editor.
///
/// <b>The owner places it; this puts the player's changes on top.</b> Whatever lays the
/// element out -- <see cref="HudView"/>, <see cref="TouchCluster"/>, <see cref="TouchLayout"/>
/// -- calls <see cref="Settle"/> when it has, which records that placement as the default and
/// re-applies the saved entry. Owners re-lay themselves (the touch cluster does whenever the
/// canvas rescales or equipment arrives), so an entry applied once would be undone the next
/// time; applied after every owner layout, it cannot be.
///
/// Registered for as long as it exists, not while active: the objective strip and the grenade
/// button hide themselves when there is nothing to show, and the editor still has to be able
/// to place them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HudLayoutTarget : MonoBehaviour
{
    [Tooltip("Permanent key in saved layouts. Renaming it forgets every layout that placed it.")]
    public string id;
    [Tooltip("What the editor calls it.")]
    public string label;
    [Tooltip("An invisible touch region rather than something drawn -- the joystick zone. It " +
             "is movable, but it is meant to lie under other things, so it never counts as an " +
             "overlap.")]
    public bool zone;

    bool _hasDefault;
    Vector2 _dMin, _dMax, _dPivot, _dPos, _dSizeDelta, _dSize;
    Vector3 _dScale = Vector3.one;
    CanvasGroup _group;
    HudLayout.Entry _applied;

    public RectTransform Rect => (RectTransform)transform;
    public bool Hidden => _applied != null && _applied.hidden;

    /// <summary>Adds (or finds) the target on an element and names it.</summary>
    public static HudLayoutTarget Mark(Component c, string id, string label)
    {
        if (c == null) return null;
        var t = c.GetComponent<HudLayoutTarget>();
        if (t == null) t = c.gameObject.AddComponent<HudLayoutTarget>();
        t.id = id;
        t.label = label;
        HudLayout.Register(t);
        return t;
    }

    void Awake() => HudLayout.Register(this);
    void OnDestroy() => HudLayout.Unregister(this);

    CanvasGroup Group
    {
        get
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            return _group;
        }
    }

    /// <summary>The owner has laid this out: that is the default now. Re-applies the saved entry.</summary>
    public void Settle()
    {
        var r = Rect;
        _dMin = r.anchorMin; _dMax = r.anchorMax; _dPivot = r.pivot;
        _dPos = r.anchoredPosition; _dSizeDelta = r.sizeDelta; _dSize = r.rect.size;
        // Scale once only. Owners re-lay position and size but never scale, so on a second
        // settle the transform is carrying the player's own scale, and taking that as the
        // default would compound it on every rebuild.
        if (!_hasDefault) _dScale = r.localScale;
        _hasDefault = true;
        Apply(HudLayout.Current.Find(id));
    }

    /// <summary>Shows an entry, or the owner's placement for none.</summary>
    public void Apply(HudLayout.Entry e)
    {
        if (!_hasDefault) { Settle(); return; }
        _applied = e;
        var r = Rect;
        r.anchorMin = _dMin; r.anchorMax = _dMax; r.pivot = _dPivot;
        r.sizeDelta = _dSizeDelta; r.anchoredPosition = _dPos; r.localScale = _dScale;

        if (e == null)
        {
            Group.alpha = 1f;
            Group.blocksRaycasts = true;
            return;
        }

        if (e.placed)
        {
            // A stretched element becomes a fixed-size one at the size it was, or a point
            // anchor would collapse it to its size delta.
            bool stretched = _dMin != _dMax;
            var a = new Vector2(e.ax, e.ay);
            r.anchorMin = r.anchorMax = r.pivot = a;
            if (stretched) r.sizeDelta = _dSize;
            r.anchoredPosition = new Vector2(e.x, e.y);
        }
        r.localScale = _dScale * Mathf.Clamp(e.scale, 0.6f, 1.5f);
        Group.alpha = e.hidden ? 0f : Mathf.Clamp(e.opacity, 0.2f, 1f);
        Group.blocksRaycasts = !e.hidden;
    }

    /// <summary>
    /// Describes where the element is now as an entry anchored to its nearest edges: the
    /// thirds of the parent its centre is in decide left/centre/right and bottom/middle/top.
    /// </summary>
    public HudLayout.Entry Measure(HudLayout.Entry into)
    {
        var e = into ?? new HudLayout.Entry { id = id };
        var r = Rect;
        if (!(r.parent is RectTransform parent)) return e;

        var corners = new Vector3[4];
        r.GetWorldCorners(corners);
        Vector2 min = parent.InverseTransformPoint(corners[0]);
        Vector2 max = parent.InverseTransformPoint(corners[2]);
        var pr = parent.rect;
        Vector2 centre = (min + max) * 0.5f;
        float fx = Mathf.InverseLerp(pr.xMin, pr.xMax, centre.x);
        float fy = Mathf.InverseLerp(pr.yMin, pr.yMax, centre.y);
        e.ax = fx < 1f / 3f ? 0f : fx > 2f / 3f ? 1f : 0.5f;
        e.ay = fy < 1f / 3f ? 0f : fy > 2f / 3f ? 1f : 0.5f;

        Vector2 point = new Vector2(Mathf.Lerp(min.x, max.x, e.ax), Mathf.Lerp(min.y, max.y, e.ay));
        Vector2 anchor = new Vector2(Mathf.Lerp(pr.xMin, pr.xMax, e.ax), Mathf.Lerp(pr.yMin, pr.yMax, e.ay));
        e.x = point.x - anchor.x;
        e.y = point.y - anchor.y;
        e.placed = true;
        return e;
    }

    /// <summary>The scale the player has chosen, relative to the owner's.</summary>
    public float PlayerScale => _dScale.x > 0.0001f ? Rect.localScale.x / _dScale.x : 1f;

    /// <summary>Moves the element's rect by a delta in its parent's units, keeping its current anchoring.</summary>
    public void Nudge(Vector2 delta) => Rect.anchoredPosition += delta;
}
