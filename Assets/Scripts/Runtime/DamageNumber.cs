using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A damage figure that floats off a hit and fades. Spawn one with
/// <see cref="Show"/> -- there is no prefab to wire.
///
/// Numbers are pooled because a full-auto rifle into a crowd produces a dozen a
/// second, and allocating a TextMeshPro object per bullet would hand the garbage
/// collector the whole fight.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class DamageNumber : MonoBehaviour
{
    [Header("Motion")]
    public float riseSpeed = 1.4f;
    public float lifetime = 0.75f;
    public float spreadRadius = 0.22f;

    [Tooltip("Size the number starts at relative to its settled size. Above 1 it pops " +
             "on impact, which is what sells a big hit.")]
    public float punchScale = 1.45f;

    static readonly Queue<DamageNumber> Pool = new Queue<DamageNumber>();
    static Transform _poolRoot;

    /// <summary>Hard cap on numbers alive at once, so a crowd cannot bury the screen.</summary>
    const int MaxLive = 48;
    static int _live;

    /// <summary>
    /// Domain reload is disabled in this project, so the pool would otherwise carry
    /// destroyed objects and a stale live count into the next play session -- and a
    /// live count stuck at the cap means no damage numbers ever appear again.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        Pool.Clear();
        _poolRoot = null;
        _live = 0;
    }

    TextMeshPro _text;
    Camera _camera;
    float _endTime;
    float _baseScale = 1f;
    Vector3 _velocity;
    Color _color;

    void Awake()
    {
        _text = GetComponent<TextMeshPro>();
        _camera = Camera.main;
    }

    /// <summary>
    /// Pops a number at a world position. Returns null when the screen is already
    /// full or TextMeshPro has no default font to draw with.
    /// </summary>
    public static DamageNumber Show(Vector3 position, float amount, Color color, float scale = 1f)
    {
        ResetIfSceneReloaded();
        if (_live >= MaxLive) return null;

        var number = Rent();
        if (number == null) return null;

        number.Play(position, Mathf.Max(1f, Mathf.Round(amount)).ToString("0"), color, scale);
        return number;
    }

    /// <summary>
    /// The pool root is a scene object, so a reload destroys it and leaves these
    /// statics describing numbers that no longer exist. Left alone, the live count
    /// stays pinned at its cap and the next run silently shows nothing at all.
    /// </summary>
    static void ResetIfSceneReloaded()
    {
        if (_poolRoot != null) return;

        Pool.Clear();
        _live = 0;
    }

    static DamageNumber Rent()
    {
        // Pooled entries do not survive a scene reload, so drop the corpses rather
        // than handing a destroyed object back to a caller.
        while (Pool.Count > 0)
        {
            var pooled = Pool.Dequeue();
            if (pooled != null) return pooled;
        }

        if (_poolRoot == null)
        {
            var rootGo = new GameObject("DamageNumbers");
            _poolRoot = rootGo.transform;
        }

        var font = TMP_Settings.defaultFontAsset;
        if (font == null) return null;

        var go = new GameObject("DamageNumber");
        go.transform.SetParent(_poolRoot, false);

        var text = go.AddComponent<TextMeshPro>();
        text.font = font;
        text.fontSize = 3.2f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        return go.AddComponent<DamageNumber>();
    }

    void Play(Vector3 position, string label, Color color, float scale)
    {
        _live++;
        gameObject.SetActive(true);

        transform.position = position + Random.insideUnitSphere * spreadRadius;
        _baseScale = scale;
        transform.localScale = Vector3.one * (scale * punchScale);

        _text.text = label;
        _color = color;
        _text.color = color;

        // Drift sideways as well as up, so two hits in the same spot stay legible.
        _velocity = new Vector3(Random.Range(-0.5f, 0.5f), riseSpeed, Random.Range(-0.5f, 0.5f));
        _endTime = Time.time + lifetime;
    }

    void LateUpdate()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        float remaining = _endTime - Time.time;
        if (remaining <= 0f)
        {
            Retire();
            return;
        }

        float t = 1f - remaining / Mathf.Max(0.0001f, lifetime);

        transform.position += _velocity * Time.deltaTime;
        _velocity.y -= 1.1f * Time.deltaTime;          // gentle arc, so it settles
        transform.rotation = _camera.transform.rotation;

        // Snap back from the impact punch quickly, then hold size while it fades.
        float scale = _baseScale * Mathf.Lerp(punchScale, 1f, Mathf.Clamp01(t * 5f));
        transform.localScale = Vector3.one * scale;

        _text.color = new Color(_color.r, _color.g, _color.b,
                                Mathf.Clamp01(1f - Mathf.InverseLerp(0.55f, 1f, t)));
    }

    void Retire()
    {
        _live = Mathf.Max(0, _live - 1);
        gameObject.SetActive(false);
        Pool.Enqueue(this);
    }
}
