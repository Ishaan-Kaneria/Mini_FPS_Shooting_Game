using UnityEngine;

/// <summary>
/// Where toasts appear: a column in a corner, newest at the top, at most
/// <see cref="maxVisible"/> at once. Anything beyond that dismisses the oldest rather than
/// queueing, because news that arrives after the moment it describes is not news.
/// </summary>
[DisallowMultipleComponent]
public class UIToastStack : MonoBehaviour
{
    [Tooltip("How many toasts can be up at once before the oldest is dismissed.")]
    [Min(1)] public int maxVisible = 3;

    [Tooltip("Which theme to read. Empty means the active one.")]
    public UITheme theme;

    UITheme Theme => theme != null ? theme : UITheme.Active;

    /// <summary>Shows a toast. Hold of zero or less keeps it until it is dismissed.</summary>
    public UIToast Push(UIToast.Kind kind, string iconId, string title, string body, float hold = -1f)
    {
        var t = Theme;
        var toast = UIKit.Toast(transform, t);
        toast.holdSeconds = hold < 0f ? t.toastSeconds : hold;
        toast.Set(t, kind, iconId, title, body);
        toast.transform.SetAsFirstSibling();

        int live = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var other = transform.GetChild(i).GetComponent<UIToast>();
            if (other == null) continue;
            if (++live > maxVisible) other.Dismiss();
        }
        return toast;
    }
}
