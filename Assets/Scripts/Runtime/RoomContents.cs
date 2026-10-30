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

    [Tooltip("Seconds between checks. A room comes into view at a walk well inside the radius, so a quarter of a second is never seen.")]
    [Range(0.05f, 1f)] public float interval = 0.25f;

    [SerializeField] Bounds[] _rooms = new Bounds[0];
    [SerializeField] int[] _first = new int[0];         // each room's first renderer in _renderers; the next room's is its end
    [SerializeField] Renderer[] _renderers = new Renderer[0];

    bool[] _on;
    float _next;

    public int RoomCount => _rooms.Length;
    public int RendererCount => _renderers.Length;

    /// <summary>The generator's: the rooms and what is in each (renderers listed room after room).</summary>
    public void Set(Bounds[] rooms, int[] first, Renderer[] renderers)
    {
        _rooms = rooms; _first = first; _renderers = renderers; _on = null;
    }

    void OnEnable() { _on = null; _next = 0f; }

    void OnDisable()
    {
        // Everything back on, so nothing is left hidden if this is switched off (a test, a debug view).
        foreach (var r in _renderers) if (r != null) r.enabled = true;
        _on = null;
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + interval;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float r2 = radius * radius;
        bool first = _on == null || _on.Length != _rooms.Length;
        if (first) _on = new bool[_rooms.Length];
        for (int i = 0; i < _rooms.Length; i++)
        {
            bool want = (_rooms[i].ClosestPoint(eye) - eye).sqrMagnitude < r2;
            if (!first && want == _on[i]) continue;
            _on[i] = want;
            int end = i + 1 < _first.Length ? _first[i + 1] : _renderers.Length;
            for (int k = _first[i]; k < end; k++)
                if (_renderers[k] != null) _renderers[k].enabled = want;
        }
    }
}
