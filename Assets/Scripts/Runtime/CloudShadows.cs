using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shadows of clouds drifting over the town: a tiling cookie on the sun, moved with the wind (Docs/DESERT_LIGHTING.md,
/// "Cloud shadows"). Wanted only under a sky that has clouds in it (LevelTheme.cloudShadows); a clear sky with patches of
/// shade crossing the ground reads as a fault.
///
/// The cookie is made here, in play, and taken off again when this is disabled: a bake never sees it, so the lightmaps and
/// probes keep the clear-sky bounce, and only the sun's real-time direct light is shaded. On Low and on phones it stays off
/// (they have no real-time sun shadows to match it to).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public class CloudShadows : MonoBehaviour
{
    [Tooltip("How much of the sun a cloud takes away at its thickest.")]
    [Range(0f, 0.9f)] public float strength = 0.35f;
    [Tooltip("Share of the sky under cloud, 0-1.")]
    [Range(0f, 1f)] public float cover = 0.35f;
    [Tooltip("Metres the pattern repeats over.")]
    [Min(50f)] public float scale = 600f;
    [Tooltip("Wind over the ground, metres per second (x east, y north).")]
    public Vector2 wind = new Vector2(3.5f, 1.2f);
    [Tooltip("Softness of a cloud's edge, as a share of the noise range. Real cloud shadows have edges tens of metres wide.")]
    [Range(0.02f, 0.4f)] public float edge = 0.14f;

    const int Size = 256;

    Light _light;
    UniversalAdditionalLightData _data;
    Texture2D _cookie;
    Texture _cookieWas;
    Vector2 _sizeWas, _offsetWas;
    bool _applied;

    // Made on demand, never in a field initialiser (domain reload is off).
    Texture2D Cookie => _cookie != null ? _cookie : (_cookie = Make(strength, cover, edge));

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (QualityTiers.Handheld || QualityTiers.Current == GameSettings.Quality.Low) return;
        _light = GetComponent<Light>();
        if (_light == null || _light.type != LightType.Directional) return;
        if (!TryGetComponent(out _data)) _data = gameObject.AddComponent<UniversalAdditionalLightData>();
        _cookieWas = _light.cookie;
        if (_data != null) { _sizeWas = _data.lightCookieSize; _offsetWas = _data.lightCookieOffset; }
        _light.cookie = Cookie;
        if (_data != null) _data.lightCookieSize = new Vector2(scale, scale);
        _applied = true;
    }

    void OnDisable()
    {
        if (!_applied) return;
        _applied = false;
        if (_light != null) _light.cookie = _cookieWas;
        if (_data != null) { _data.lightCookieSize = _sizeWas; _data.lightCookieOffset = _offsetWas; }
    }

    void OnDestroy()
    {
        if (_cookie != null) Destroy(_cookie);
        _cookie = null;
    }

    void Update()
    {
        if (!_applied || _data == null) return;
        // Repeat the offset so it never grows large enough to lose float precision over a long level.
        Vector2 o = wind * Time.time;
        _data.lightCookieOffset = new Vector2(Mathf.Repeat(o.x, scale), Mathf.Repeat(o.y, scale));
    }

    /// <summary>A tiling cloud pattern: periodic value noise in five octaves, cut at the cover, 1 in the clear and 1 - strength under a cloud.</summary>
    static Texture2D Make(float strength, float cover, float edge)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
        {
            name = "CloudShadows",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.DontSave,
        };
        var rng = new System.Random(4127);
        const int Octaves = 5;
        var lattices = new float[Octaves][];
        for (int o = 0; o < Octaves; o++)
        {
            int p = 4 << o;
            var l = new float[p * p];
            for (int i = 0; i < l.Length; i++) l[i] = (float)rng.NextDouble();
            lattices[o] = l;
        }
        var values = new float[Size * Size];
        float min = float.MaxValue, max = float.MinValue;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float sum = 0f, amp = 1f;
                for (int o = 0; o < Octaves; o++)
                {
                    int p = 4 << o;
                    float fx = x * p / (float)Size, fy = y * p / (float)Size;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = fx - x0, ty = fy - y0;
                    tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                    var l = lattices[o];
                    float a = l[(y0 % p) * p + x0 % p], b = l[(y0 % p) * p + (x0 + 1) % p];
                    float c = l[((y0 + 1) % p) * p + x0 % p], d = l[((y0 + 1) % p) * p + (x0 + 1) % p];
                    sum += amp * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    amp *= 0.5f;
                }
                values[y * Size + x] = sum;
                if (sum < min) min = sum;
                if (sum > max) max = sum;
            }
        // The cover as a cut through the noise's own spread: a share `cover` of the texels end up under cloud.
        var sorted = (float[])values.Clone();
        System.Array.Sort(sorted);
        float cut = sorted[Mathf.Clamp(Mathf.RoundToInt((1f - cover) * (sorted.Length - 1)), 0, sorted.Length - 1)];
        float soft = Mathf.Max(1e-4f, edge * (max - min));
        var px = new Color32[Size * Size];
        for (int i = 0; i < px.Length; i++)
        {
            float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut - soft, cut + soft, values[i]));
            byte v = (byte)Mathf.RoundToInt(255f * (1f - strength * cloud));
            px[i] = new Color32(v, v, v, v);
        }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }
}
