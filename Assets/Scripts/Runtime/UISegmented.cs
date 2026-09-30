using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A row of two to four options, every one visible, the chosen one filled amber:
/// [ LOW | MEDIUM | HIGH ]. Settings uses it for every short choice, in place of the
/// arrow chooser (<see cref="UIChoice"/>), which hid all but one option and made the player
/// step through them to find out what there was.
///
/// Like the chooser, the whole control is the one selectable, so a pad walks down the page
/// one row at a time: left and right step the choice, A steps it forward. The segments take
/// clicks and taps but never the focus.
/// </summary>
public class UISegmented : Selectable, IMoveHandler, ISubmitHandler
{
    public FlatRect face;
    public Button[] segments = Array.Empty<Button>();
    public FlatRect[] segmentFaces = Array.Empty<FlatRect>();
    public TMP_Text[] segmentLabels = Array.Empty<TMP_Text>();

    [SerializeField] int _index;
    public event Action<int> Changed;

    [NonSerialized] bool _wired;
    int _hover = -1;

    public int Index
    {
        get => _index;
        set => Set(value, notify: false);
    }

    public int Count => segments != null ? segments.Length : 0;

    public void Set(int index, bool notify)
    {
        if (Count == 0) return;
        index = Mathf.Clamp(index, 0, Count - 1);
        bool changed = index != _index;
        _index = index;
        Paint();
        if (changed && notify)
        {
            UISfx.Play(UISfx.Sound.Toggle);
            Changed?.Invoke(index);
        }
    }

    /// <summary>Wires the segments, once. Called by the kit after it assigns them (Awake runs before).</summary>
    public void Wire()
    {
        if (_wired || segments == null || segments.Length == 0) return;
        _wired = true;
        for (int i = 0; i < segments.Length; i++)
        {
            int k = i;
            if (segments[i] == null) continue;
            segments[i].onClick.AddListener(() => { if (IsInteractable()) Set(k, notify: true); });
            var hover = segments[i].gameObject.AddComponent<SegmentHover>();
            hover.owner = this;
            hover.index = k;
        }
        Paint();
    }

    protected override void Awake()
    {
        transition = Transition.None;
        base.Awake();
        Wire();
    }

    public override void OnMove(AxisEventData e)
    {
        if (!IsInteractable()) { base.OnMove(e); return; }
        if (e.moveDir == MoveDirection.Left && _index > 0) { Set(_index - 1, true); return; }
        if (e.moveDir == MoveDirection.Right && _index < Count - 1) { Set(_index + 1, true); return; }
        if (e.moveDir == MoveDirection.Left || e.moveDir == MoveDirection.Right) return;
        base.OnMove(e);
    }

    public void OnSubmit(BaseEventData e)
    {
        if (IsInteractable() && Count > 0) Set((_index + 1) % Count, true);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        Paint();
    }

    internal void Hover(int index, bool on)
    {
        _hover = on ? index : (_hover == index ? -1 : _hover);
        Paint();
    }

    void Paint()
    {
        var t = UITheme.Active;
        bool disabled = !IsInteractable();
        if (face != null)
            face.borderColor = currentSelectionState == SelectionState.Highlighted ? t.borderHover : t.border;
        for (int i = 0; i < Count; i++)
        {
            bool on = i == _index;
            if (i < segmentFaces.Length && segmentFaces[i] != null)
                segmentFaces[i].color = on ? (disabled ? t.panelHover : t.accent)
                                      : i == _hover && !disabled ? t.panelHover : new Color(0, 0, 0, 0);
            if (i < segmentLabels.Length && segmentLabels[i] != null)
                segmentLabels[i].color = disabled ? t.textDisabled : on ? t.textOnAccent
                                       : i == _hover ? t.textPrimary : t.textSecondary;
        }
    }

    /// <summary>Hover on one segment, reported to the control that paints it.</summary>
    public class SegmentHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public UISegmented owner;
        public int index;
        public void OnPointerEnter(PointerEventData e) { if (owner != null) owner.Hover(index, true); }
        public void OnPointerExit(PointerEventData e) { if (owner != null) owner.Hover(index, false); }
    }
}
