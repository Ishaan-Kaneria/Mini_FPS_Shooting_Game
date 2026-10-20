using UnityEngine;

/// <summary>
/// One signalled junction in the desert town: two phases (traffic along x, traffic along z), each green, amber, then
/// all red for a moment, the lamps on every head switched to match. <see cref="TrafficCar"/> asks it whether its
/// approach may go. Junctions are offset in their cycle so the avenue does not change all at once.
/// </summary>
public class TownSignals : MonoBehaviour
{
    [Tooltip("Seconds of green for each direction.")]
    public float green = 14f;
    [Tooltip("Seconds of amber after green.")]
    public float amber = 3f;
    [Tooltip("Seconds with both directions red between phases.")]
    public float allRed = 1.5f;
    [Tooltip("Where in the cycle this junction starts, in seconds.")]
    public float offset;

    [Header("Lamps (written by the builder)")]
    public Renderer[] xRed, xAmber, xGreen, zRed, zAmber, zGreen;
    public Material red, amberLit, greenLit, off;

    public enum Light { Red, Amber, Green }

    Light _x = (Light)(-1), _z = (Light)(-1);

    float Cycle => 2f * (green + amber + allRed);

    /// <summary>What the signal shows the traffic along x (or along z).</summary>
    public Light Showing(bool alongX)
    {
        float t = Mathf.Repeat(Time.time + offset, Cycle);
        float phase = green + amber + allRed;
        bool first = t < phase;
        float local = first ? t : t - phase;
        bool mine = first == alongX;
        if (!mine) return Light.Red;
        return local < green ? Light.Green : local < green + amber ? Light.Amber : Light.Red;
    }

    /// <summary>Whether a car on that approach may enter the junction. Amber counts as stop unless already committed.</summary>
    public bool CanGo(bool alongX) => Showing(alongX) == Light.Green;

    void Update()
    {
        var x = Showing(true);
        var z = Showing(false);
        if (x != _x) { Apply(xRed, xAmber, xGreen, x); _x = x; }
        if (z != _z) { Apply(zRed, zAmber, zGreen, z); _z = z; }
    }

    void Apply(Renderer[] r, Renderer[] a, Renderer[] g, Light l)
    {
        Set(r, l == Light.Red ? red : off);
        Set(a, l == Light.Amber ? amberLit : off);
        Set(g, l == Light.Green ? greenLit : off);
    }

    static void Set(Renderer[] rs, Material m)
    {
        if (rs == null || m == null) return;
        foreach (var r in rs) if (r != null) r.sharedMaterial = m;
    }
}
