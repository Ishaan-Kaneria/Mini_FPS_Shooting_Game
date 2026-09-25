using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A row of <see cref="FlatButton"/> tabs, exactly one of which is showing.
///
/// The selected tab is marked by colour and by an amber line under it -- two signals, so the
/// state survives a player who cannot tell amber from the grey beside it. The line sits on
/// the bar's own one-pixel rule, which is what makes it read as a tab rather than as an
/// underlined word.
/// </summary>
[DisallowMultipleComponent]
public class UITabBar : MonoBehaviour
{
    [Serializable] public class IndexEvent : UnityEvent<int> { }

    [Tooltip("The tabs, in order.")]
    public FlatButton[] tabs = Array.Empty<FlatButton>();

    [SerializeField] int _selected;

    [Tooltip("Raised when the player picks a different tab. Not raised by setting Selected " +
             "from code, so a screen restoring its last tab does not re-run its own handler.")]
    public IndexEvent onChanged = new IndexEvent();

    public int Selected
    {
        get => _selected;
        set => Select(value, notify: false);
    }

    // Which tabs already have a click listener. Not serialized: runtime listeners do not
    // survive a domain reload either, so after one both start again from nothing.
    [NonSerialized] System.Collections.Generic.HashSet<FlatButton> _wired;

    /// <summary>
    /// Gives every tab its click listener, once. <b>Not only in Awake</b>: UIKit adds this
    /// component and assigns the tabs afterwards, so for anything built at runtime -- Settings,
    /// the HUD editor, the achievement filters -- Awake ran on an empty list and no tab ever
    /// answered a click. Safe to call again; UIKit calls it straight after assigning.
    /// </summary>
    public void Wire()
    {
        if (_wired == null) _wired = new System.Collections.Generic.HashSet<FlatButton>();
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            if (tabs[i] != null && _wired.Add(tabs[i])) tabs[i].onClick.AddListener(() => Select(index, notify: true));
        }
    }

    void Awake() => Wire();
    void Start() => Wire();

    void OnEnable()
    {
        Wire();
        Refresh();
    }

    public void Select(int index, bool notify)
    {
        if (tabs.Length == 0) return;
        index = Mathf.Clamp(index, 0, tabs.Length - 1);
        bool changed = index != _selected;
        _selected = index;
        Refresh();
        if (changed && notify) onChanged.Invoke(index);
    }

    void Refresh()
    {
        for (int i = 0; i < tabs.Length; i++)
            if (tabs[i] != null) tabs[i].Selected = i == _selected;
    }
}
