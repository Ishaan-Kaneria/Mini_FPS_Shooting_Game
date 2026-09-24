using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks the control a gamepad should land on when this screen comes up -- the PLAY button,
/// the first unlocked level, the BUY on the card that is showing. <see cref="UINavigator"/>
/// takes the highest-priority visible one, so an overlay's default wins over the screen
/// under it simply by carrying a higher priority, or by being the only one not covered.
/// </summary>
[DisallowMultipleComponent]
public class UIDefaultSelection : MonoBehaviour
{
    [Tooltip("Higher wins when more than one is visible. Overlays should outrank the screen under them.")]
    public int priority;

    static readonly List<UIDefaultSelection> _live = new List<UIDefaultSelection>();

    /// <summary>Every enabled marker. Read by <see cref="UINavigator"/>.</summary>
    public static IReadOnlyList<UIDefaultSelection> Live => _live;

    void OnEnable() => _live.Add(this);
    void OnDisable() => _live.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _live.Clear();
}
