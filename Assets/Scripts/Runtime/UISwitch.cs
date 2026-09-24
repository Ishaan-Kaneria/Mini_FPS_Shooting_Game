using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// An on/off switch: a flat track with a square knob, amber when on. Clicked, tapped, or
/// pressed with A while the focus ring is on it.
///
/// A <see cref="Selectable"/> so the gamepad can land on it and the focus ring can find it;
/// its own graphic is the raycast target and nothing inside it is, the rule every button
/// in the kit follows.
/// </summary>
public class UISwitch : Selectable, IPointerClickHandler, ISubmitHandler
{
    public FlatRect track;
    public FlatRect knob;
    public TMP_Text stateLabel;

    [SerializeField] bool _on;
    public event Action<bool> Changed;

    float _shown = -1f;

    public bool IsOn
    {
        get => _on;
        set => Set(value, notify: false);
    }

    public void Set(bool on, bool notify)
    {
        bool changed = on != _on;
        _on = on;
        if (!Application.isPlaying) _shown = on ? 1f : 0f;
        Paint();
        if (changed && notify) Changed?.Invoke(on);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
        Set(!_on, notify: true);
    }

    public void OnSubmit(BaseEventData e)
    {
        if (!IsInteractable()) return;
        Set(!_on, notify: true);
    }

    protected override void Awake()
    {
        transition = Transition.None;
        base.Awake();
    }

    void Update()
    {
        float target = _on ? 1f : 0f;
        if (Mathf.Approximately(_shown, target)) return;
        float d = Mathf.Max(0.001f, UITheme.Active.motionFast);
        _shown = _shown < 0f ? target : Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime / d);
        Paint();
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        Paint();
    }

    void Paint()
    {
        var t = UITheme.Active;
        float k = _shown < 0f ? (_on ? 1f : 0f) : UITheme.EaseOut(_shown);
        bool disabled = !IsInteractable();
        bool hover = currentSelectionState == SelectionState.Highlighted;
        if (track != null)
        {
            track.color = Color.Lerp(t.background, t.accent, disabled ? 0f : k);
            track.borderColor = k > 0.5f ? t.accent : hover ? t.borderHover : t.border;
        }
        if (knob != null)
        {
            var rt = knob.rectTransform;
            rt.anchorMin = new Vector2(Mathf.Lerp(0f, 0.5f, k), 0f);
            rt.anchorMax = new Vector2(Mathf.Lerp(0.5f, 1f, k), 1f);
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            knob.color = disabled ? t.textDisabled : k > 0.5f ? t.textOnAccent : t.textSecondary;
        }
        if (stateLabel != null)
        {
            stateLabel.text = _on ? "ON" : "OFF";
            stateLabel.color = disabled ? t.textDisabled : _on ? t.textPrimary : t.textSecondary;
        }
    }
}
