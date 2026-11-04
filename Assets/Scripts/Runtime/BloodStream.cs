using UnityEngine;

/// <summary>
/// An open artery's stream: one connected line of liquid, each point thrown from the wound with its own speed and thickness and
/// falling under gravity, so a heartbeat is seen travelling down the stream as it swells. A line and not a particle ribbon,
/// which shared the kit's particle system with every other wound and could not be kept in one piece. One only for each
/// open wound, destroyed with it. Holds no static state.
/// </summary>
public class BloodStream : MonoBehaviour
{
    const int MaxPoints = 48;
    const float Seconds = 0.55f, Gravity = 11f;

    readonly Vector3[] _at = new Vector3[MaxPoints], _speed = new Vector3[MaxPoints];
    readonly float[] _born = new float[MaxPoints], _width = new float[MaxPoints];
    readonly Keyframe[] _keys = new Keyframe[MaxPoints];
    int _first, _count;
    float _nextPoint;
    LineRenderer _line;
    Vector3[] _drawn = new Vector3[MaxPoints];

    /// <summary>Points in the stream drawn now, for the checks.</summary>
    public int Drawn => _line != null && _line.enabled ? _line.positionCount : 0;
    /// <summary>Its widest now, metres, for the checks.</summary>
    public float Widest { get; private set; }

    public static BloodStream Create(Transform parent, Material material, Color colour)
    {
        var go = new GameObject("BloodStream");
        go.transform.SetParent(parent, false);
        var stream = go.AddComponent<BloodStream>();
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.startColor = line.endColor = colour;
        line.numCapVertices = 2;
        line.numCornerVertices = 1;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        line.positionCount = 0;
        stream._line = line;
        return stream;
    }

    /// <summary>One frame of the wound: the mouth of the stream, its way, how hard, and how thick there.</summary>
    public void Feed(Vector3 mouth, Vector3 velocity, float width)
    {
        if (Time.time < _nextPoint) { Fresh(mouth); return; }
        _nextPoint = Time.time + 1f / 60f;
        int slot = (_first + _count) % MaxPoints;
        if (_count == MaxPoints) _first = (_first + 1) % MaxPoints; else _count++;
        _at[slot] = mouth;
        _speed[slot] = velocity;
        _born[slot] = Time.time;
        _width[slot] = width;
    }

    void Fresh(Vector3 mouth)
    {
        if (_count > 0) _at[(_first + _count - 1) % MaxPoints] = mouth;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        while (_count > 0 && Time.time - _born[_first] > Seconds) { _first = (_first + 1) % MaxPoints; _count--; }
        if (_line == null) return;
        if (_count < 2) { _line.positionCount = 0; Widest = 0f; return; }

        float widest = 0f;
        for (int n = 0; n < _count; n++)
        {
            int i = (_first + _count - 1 - n) % MaxPoints;      // the newest, at the wound, first
            _speed[i] += Vector3.down * Gravity * dt;
            _at[i] += _speed[i] * dt;
            _drawn[n] = _at[i];
            // Thins as it travels and breaks up, to nothing at its far end.
            float along = n / (float)(_count - 1);
            float w = _width[i] * (1f - along) * (1f - along * 0.4f);
            if (w > widest) widest = w;
            _keys[n] = new Keyframe(along, w);
        }
        Widest = widest;
        _line.positionCount = _count;
        _line.SetPositions(_drawn);
        _line.widthMultiplier = 1f;
        var keys = new Keyframe[_count];
        System.Array.Copy(_keys, keys, _count);
        _line.widthCurve = new AnimationCurve(keys);
    }
}
