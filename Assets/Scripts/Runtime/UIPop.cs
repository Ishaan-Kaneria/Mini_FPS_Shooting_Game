using UnityEngine;

/// <summary>
/// A thing that has just happened says so with one short pop: it arrives a little large and
/// settles to its size. A star landing on the results card, a key on the on-screen keyboard.
///
/// Scale only, over the theme's standard motion time, with no overshoot -- the interface
/// does not bounce. Unscaled time, because the results screen is shown with the clock
/// stopped. Safe on children of layout groups, which own position but never scale.
/// </summary>
[DisallowMultipleComponent]
public class UIPop : MonoBehaviour
{
    float _from = 1f, _t = 1f;

    /// <summary>Pops a transform from <paramref name="from"/> times its size back to its size.</summary>
    public static void Play(Component c, float from = 1.45f)
    {
        if (c == null) return;
        var pop = c.GetComponent<UIPop>();
        if (pop == null) pop = c.gameObject.AddComponent<UIPop>();
        pop._from = from;
        pop._t = 0f;
        pop.Apply();
    }

    void Update()
    {
        if (_t >= 1f) return;
        float d = Mathf.Max(0.01f, UITheme.Active.motionStandard * 1.5f);
        _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / d);
        Apply();
    }

    void Apply()
    {
        float k = Mathf.Lerp(_from, 1f, UITheme.EaseOut(_t));
        transform.localScale = new Vector3(k, k, 1f);
    }
}
