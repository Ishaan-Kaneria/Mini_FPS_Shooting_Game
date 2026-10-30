using UnityEngine;

/// <summary>
/// The furniture and shop goods inside the town's rooms, drawn only near their room. Measured 2026-10-29 (FrameCostProbe,
/// Medium): from the street, 128 of the 912 rooms' contents were drawn a view on average (up to 273), behind walls or a few
/// pixels through a door, 13,333 renderers and 72 million triangles in all, casting shadows. A room's contents are drawn
/// while the eye is within <see cref="radius"/> of its box: inside it, at its door, or looking in through a window up
/// close. The rooms and what is in each are found by the desert's generator (FPSKitTownGI, the same boxes as the rooms'
/// reflection probes) and saved here, so the phone and web copy, which drops those probes, keeps this. Holds no static state.
/// </summary>
[DisallowMultipleComponent]
public class RoomContents : MonoBehaviour
{
    [Tooltip("Metres from a room (its box) inside which what is in it is drawn. Past it the room is behind its walls, or a few pixels through a door.")]
    [Min(5f)] public float radius = 30f;

    [Tooltip("Metres from a house inside which its own inside (floors, ceilings, partitions, stairs) is drawn. Further than the furniture's, so a doorway seen across a street never shows through an empty house.")]
    [Min(5f)] public float shellRadius = 60f;

    [Tooltip("Seconds between checks. A room comes into view at a walk well inside the radius, so a quarter of a second is never seen.")]
    [Range(0.05f, 1f)] public float interval = 0.25f;

    [SerializeField] Bounds[] _rooms = new Bounds[0];
    [SerializeField] int[] _first = new int[0];         // each room's first renderer in _renderers; the next room's is its end
    [SerializeField] Renderer[] _renderers = new Renderer[0];
    [SerializeField] int[] _shellFirst = new int[0];    // the same for each room's house inside (FPSKitTownInterior), drawn to shellRadius
    [SerializeField] Renderer[] _shells = new Renderer[0];

    bool[] _on, _shellOn;
    float _next;

    public int RoomCount => _rooms.Length;
    public int RendererCount => _renderers.Length;

    /// <summary>The generator's: the rooms, what is in each and each one's house inside (renderers listed room after room).</summary>
    public void Set(Bounds[] rooms, int[] first, Renderer[] renderers, int[] shellFirst, Renderer[] shells)
    {
        _rooms = rooms; _first = first; _renderers = renderers; _shellFirst = shellFirst; _shells = shells; _on = _shellOn = null;
    }

    public int ShellCount => _shells.Length;

    void OnEnable() { _on = null; _next = 0f; }

    void OnDisable()
    {
        // Everything back on, so nothing is left hidden if this is switched off (a test, a debug view).
        foreach (var r in _renderers) if (r != null) r.enabled = true;
        foreach (var r in _shells) if (r != null) r.enabled = true;
        _on = _shellOn = null;
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + interval;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float r2 = radius * radius, s2 = shellRadius * shellRadius;
        bool first = _on == null || _on.Length != _rooms.Length;
        if (first) { _on = new bool[_rooms.Length]; _shellOn = new bool[_rooms.Length]; }
        bool shells = _shellFirst != null && _shellFirst.Length == _rooms.Length;
        for (int i = 0; i < _rooms.Length; i++)
        {
            float d2 = (_rooms[i].ClosestPoint(eye) - eye).sqrMagnitude;
            Toggle(d2 < r2, ref _on[i], first, _first, _renderers, i);
            if (shells) Toggle(d2 < s2, ref _shellOn[i], first, _shellFirst, _shells, i);
        }
    }

    static void Toggle(bool want, ref bool on, bool first, int[] starts, Renderer[] list, int i)
    {
        if (!first && want == on) return;
        on = want;
        int end = i + 1 < starts.Length ? starts[i + 1] : list.Length;
        for (int k = starts[i]; k < end; k++)
            if (list[k] != null) list[k].enabled = want;
    }
}
