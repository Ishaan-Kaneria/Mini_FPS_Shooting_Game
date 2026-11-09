using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The town's lamps as real lights (the desert's lighting, layer 3: FPSKitDesertLighting): each round ceiling lamp in a house
/// and each tube in a shop has a light where the fitting is, placed by the generator. There are well over a thousand, so only
/// the <see cref="MaxOn"/> nearest the eye within <see cref="Radius"/> are on at a time (a room's lamp is seen from inside it
/// or through its door); the rest are off, their fittings still glowing. Checked a few times a second.
///
/// One per scene that has lamps; its statics (the registry and the budget) are cleared on load (domain reload is off).
/// </summary>
[DisallowMultipleComponent]
public class LampSet : MonoBehaviour
{
    [SerializeField] Light[] _lights = new Light[0];

    /// <summary>At most this many lamps lit at once, the nearest first.</summary>
    public static int MaxOn = 24;
    /// <summary>Metres from the eye past which no lamp is lit.</summary>
    public static float Radius = 35f;
    /// <summary>Off: every lamp dark (the views' "layer 2 only" pictures).</summary>
    public static bool Active = true;

    static readonly List<LampSet> Loaded = new List<LampSet>();
    static readonly List<(float d2, Light l)> Near = new List<(float, Light)>();
    static float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Loaded.Clear(); Near.Clear(); _next = 0f; MaxOn = 24; Radius = 35f; Active = true; }

    public int Count => _lights.Length;
    public void Set(Light[] lights) => _lights = lights;

    void OnEnable() { if (!Loaded.Contains(this)) Loaded.Add(this); foreach (var l in _lights) if (l != null) l.enabled = false; _next = 0f; }
    void OnDisable() => Loaded.Remove(this);

    void Update()
    {
        if (Loaded.Count == 0 || Loaded[0] != this || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float r2 = Radius * Radius;
        Near.Clear();
        foreach (var set in Loaded)
            foreach (var l in set._lights)
            {
                if (l == null) continue;
                float d2 = (l.transform.position - eye).sqrMagnitude;
                if (Active && d2 <= r2) Near.Add((d2, l)); else if (l.enabled) l.enabled = false;
            }
        Near.Sort((a, b) => a.d2.CompareTo(b.d2));
        for (int i = 0; i < Near.Count; i++)
        {
            bool on = i < MaxOn;
            if (Near[i].l.enabled != on) Near[i].l.enabled = on;
        }
    }
}
