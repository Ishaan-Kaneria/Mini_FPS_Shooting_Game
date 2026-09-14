using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One on-screen action button.</summary>
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public enum ActionKind { Fire, Aim, Jump, Sprint, Crouch, Reload }

    public ActionKind action = ActionKind.Fire;

    [Tooltip("Stays on until tapped again. Suits sprint and crouch; leave off for fire.")]
    public bool toggle;

    [Tooltip("Optional tint applied while held or toggled on.")]
    public Graphic target;
    public Color activeColor = new Color(1f, 1f, 1f, 0.85f);

    Color _idleColor;
    bool _on;

    void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();
        if (target != null) _idleColor = target.color;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (toggle) SetState(!_on);
        else SetState(true);

        // Edge-triggered actions fire on press only.
        if (action == ActionKind.Jump) MobileInput.QueueJump();
        else if (action == ActionKind.Reload) MobileInput.QueueReload();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!toggle) SetState(false);
    }

    void SetState(bool on)
    {
        _on = on;

        switch (action)
        {
            case ActionKind.Fire: MobileInput.Fire = on; break;
            case ActionKind.Aim: MobileInput.Aim = on; break;
            case ActionKind.Sprint: MobileInput.Sprint = on; break;
            case ActionKind.Crouch: MobileInput.Crouch = on; break;
        }

        if (target != null) target.color = on ? activeColor : _idleColor;
    }

    void OnDisable()
    {
        if (_on) SetState(false);
    }
}
