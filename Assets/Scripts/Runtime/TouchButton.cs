using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One on-screen action button.
///
/// The fire button is not quite like the others, and the difference is the whole
/// reason a phone can be a shooter at all -- see <see cref="dragFire"/>.
/// </summary>
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public enum ActionKind
    {
        Fire, Aim, Jump, Sprint, Crouch, Reload, Pause,

        /// <summary>
        /// Held to aim a bomb and released to throw it, exactly like the key. Held
        /// rather than queued on purpose: releasing *is* the throw, so a one-shot tap
        /// could not express it at all.
        /// </summary>
        Bomb,

        /// <summary>Drinks one off the belt. Edge-triggered, like reload.</summary>
        UseItem,

        /// <summary>
        /// Punches whoever is in reach. Edge-triggered. Last in the list because the
        /// kind is serialized by number into every staged touch scene.
        /// </summary>
        Melee
    }

    public ActionKind action = ActionKind.Fire;

    [Tooltip("Stays on until tapped again. Suits sprint and crouch; leave off for fire.")]
    public bool toggle;

    [Tooltip("Keep the action held while the thumb slides off this button, and turn the " +
             "view with that slide.\n\n" +
             "Meant for FIRE, and it is the difference between a playable shooter and an " +
             "unplayable one. A phone has two thumbs: the left one moves, so the right " +
             "one has to both aim and shoot. If holding the fire button occupies it, " +
             "every fight becomes a choice between firing at where the enemy was and " +
             "tracking them without firing -- and the player experiences that as the " +
             "game being unresponsive, not as a layout problem.\n\n" +
             "Press and drag is one gesture: the trigger stays down and the drag steers, " +
             "which is what a mouse has always done and what a phone otherwise has no " +
             "answer for. Every competitive mobile shooter works this way.")]
    public bool dragFire;

    [Tooltip("Optional. Supplies the look sensitivity for a drag that started on this " +
             "button, so it matches the look area exactly. Without one it still works, " +
             "at the unscaled default.")]
    public TouchProfile profile;

    [Tooltip("Optional tint applied while held or toggled on.")]
    public Graphic target;
    public Color activeColor = new Color(1f, 1f, 1f, 0.85f);

    Color _idleColor;
    bool _on;

    /// <summary>The finger that pressed this button, so a second one cannot release it.</summary>
    int _pointerId = int.MinValue;

    void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();
        if (target != null) _idleColor = target.color;
    }

    void Start() => EnforceMinimumSize();

    /// <summary>
    /// Grows the button until it is at least the profile's minimum physical size.
    ///
    /// A canvas scaled to a reference resolution draws a button that is correct in
    /// canvas units and whatever size the panel makes of it in life -- so the same
    /// layout is comfortable on a 6" phone and 5mm across on a small dense one. Below
    /// roughly 9mm the miss rate climbs sharply, and a missed FIRE button in a firefight
    /// is indistinguishable from the game ignoring the player.
    ///
    /// It only ever grows. Shrinking a button to the minimum would quietly undo a layout
    /// that deliberately made the two most-used controls the largest.
    /// </summary>
    void EnforceMinimumSize()
    {
        if (profile == null) return;

        var rect = transform as RectTransform;
        if (rect == null) return;

        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;

        float minimum = TouchMetrics.MinimumTouchSize(profile.minButtonMm, scale);
        Vector2 size = rect.sizeDelta;

        if (size.x >= minimum && size.y >= minimum) return;

        rect.sizeDelta = new Vector2(Mathf.Max(size.x, minimum), Mathf.Max(size.y, minimum));
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // A button already under one thumb ignores a second. Without this, two fingers
        // on FIRE means the first to lift switches the trigger off while the other is
        // still pressing it -- which happens constantly in the middle of a fight, and
        // reads as the gun jamming.
        if (_pointerId != int.MinValue) return;
        _pointerId = eventData.pointerId;

        if (toggle) SetState(!_on);
        else SetState(true);

        // Edge-triggered actions fire on press only.
        if (action == ActionKind.Jump) MobileInput.QueueJump();
        else if (action == ActionKind.Reload) MobileInput.QueueReload();
        else if (action == ActionKind.Pause) MobileInput.QueuePause();
        else if (action == ActionKind.UseItem) MobileInput.QueueUseItem();
        else if (action == ActionKind.Melee) MobileInput.QueueMelee();
    }

    /// <summary>
    /// Turns the view with a drag that began on this button, while the action stays
    /// held.
    ///
    /// The delta goes through the same density correction the look area uses, so a
    /// gesture that starts on the fire button and one that starts beside it turn the
    /// view by exactly the same amount. They would otherwise be two sensitivities the
    /// player switches between without ever being told.
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!dragFire || eventData.pointerId != _pointerId) return;

        float scale = profile != null ? profile.LookScaleFor(MobileInput.Aim) : 1f;
        MobileInput.AddLook(TouchMetrics.ToReferencePixels(eventData.delta) * scale);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        _pointerId = int.MinValue;

        if (!toggle) SetState(false);
    }

    void SetState(bool on)
    {
        _on = on;

        switch (action)
        {
            case ActionKind.Fire: MobileInput.PressFire(on); break;
            case ActionKind.Aim: MobileInput.Aim = on; break;
            case ActionKind.Sprint: MobileInput.SetSprintButton(on); break;
            case ActionKind.Crouch: MobileInput.Crouch = on; break;
            case ActionKind.Bomb: MobileInput.BombAim = on; break;
        }

        if (target != null) target.color = on ? activeColor : _idleColor;
    }

    void OnDisable()
    {
        _pointerId = int.MinValue;
        if (_on) SetState(false);
    }
}
