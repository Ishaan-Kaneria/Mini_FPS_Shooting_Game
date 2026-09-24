using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One of a few named options, stepped through with arrows either side of the value:
/// LOW  MEDIUM  HIGH, HOLD  TAP, 30  60.
///
/// Arrows rather than a dropdown, because a dropdown is three presses on a pad and a
/// covered screen on a phone, and the lists here are two or three long. The whole row is
/// the one selectable: left and right on the stick or d-pad step it, A steps it forward,
/// and the arrows are clickable for a pointer but never take the focus -- two extra stops
/// per row would make a pad player walk past every arrow to reach the next setting.
/// </summary>
public class UIChoice : Selectable, IMoveHandler, ISubmitHandler
{
    public TMP_Text valueLabel;
    public Button previous;
    public Button next;
    public FlatRect face;

    public string[] options = Array.Empty<string>();
    [SerializeField] int _index;
    public event Action<int> Changed;

    public int Index
    {
        get => _index;
        set => Set(value, notify: false);
    }

    public void Set(int index, bool notify)
    {
        if (options.Length == 0) return;
        index = Mathf.Clamp(index, 0, options.Length - 1);
        bool changed = index != _index;
        _index = index;
        Paint();
        if (changed && notify) Changed?.Invoke(index);
    }

    public void Step(int delta)
    {
        if (!IsInteractable() || options.Length == 0) return;
        Set(Mathf.Clamp(_index + delta, 0, options.Length - 1), notify: true);
    }

    protected override void Awake()
    {
        transition = Transition.None;
        base.Awake();
        if (previous != null) previous.onClick.AddListener(() => Step(-1));
        if (next != null) next.onClick.AddListener(() => Step(1));
    }

    public override void OnMove(AxisEventData e)
    {
        if (e.moveDir == MoveDirection.Left) { Step(-1); return; }
        if (e.moveDir == MoveDirection.Right) { Step(1); return; }
        base.OnMove(e);
    }

    public void OnSubmit(BaseEventData e)
    {
        if (options.Length == 0) return;
        Set((_index + 1) % options.Length, notify: true);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        Paint();
    }

    void Paint()
    {
        var t = UITheme.Active;
        bool disabled = !IsInteractable();
        if (valueLabel != null && options.Length > 0)
        {
            valueLabel.text = options[Mathf.Clamp(_index, 0, options.Length - 1)];
            valueLabel.color = disabled ? t.textDisabled : t.textPrimary;
        }
        if (face != null)
        {
            face.color = t.background;
            face.borderColor = currentSelectionState == SelectionState.Highlighted ? t.borderHover : t.border;
        }
        if (previous != null) previous.interactable = !disabled && _index > 0;
        if (next != null) next.interactable = !disabled && _index < options.Length - 1;
    }
}
