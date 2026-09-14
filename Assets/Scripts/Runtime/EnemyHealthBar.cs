using UnityEngine;

/// <summary>
/// A billboarded health bar floating over one enemy, built from quads rather than a
/// world-space Canvas -- a Canvas per enemy would put a layout rebuild and a draw
/// call on every body in the arena.
///
/// It stays hidden until the thing is actually hurt, so a full-strength crowd does
/// not turn into a wall of green. Shield sits as a second segment on top of health,
/// because "break the armour, then kill it" only reads if you can see both.
/// </summary>
[RequireComponent(typeof(Health))]
public class EnemyHealthBar : MonoBehaviour
{
    /// <summary>Name of the child everything the bar owns lives under.</summary>
    public const string RootName = "HealthBar";

    [Header("Placement")]
    [Tooltip("Metres above the enemy's origin. Should clear the head.")]
    public float heightOffset = 2.5f;

    public Vector2 size = new Vector2(1.1f, 0.13f);

    [Header("Visibility")]
    [Tooltip("Stay invisible until the first hit lands. A crowd of untouched enemies " +
             "wearing full bars is noise, not information.")]
    public bool hideWhenFull = true;

    [Tooltip("Always visible, whatever its health. Turn this on for a boss or an elite.")]
    public bool alwaysVisible;

    [Tooltip("Metres past which the bar stops drawing.")]
    public float maxVisibleDistance = 45f;

    [Header("Colours")]
    public Color healthyColor = new Color(0.35f, 0.85f, 0.35f);
    public Color hurtColor = new Color(0.95f, 0.75f, 0.2f);
    public Color criticalColor = new Color(0.9f, 0.25f, 0.2f);
    public Color shieldColor = new Color(0.4f, 0.75f, 1f);
    public Color backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);

    [Header("Wiring")]
    [Tooltip("Unlit material for the bar quads. Left empty, one is found at runtime -- " +
             "assign the generated asset instead so the shader survives a player build.")]
    public Material barMaterial;

    Health _health;
    Transform _root;
    Transform _fill;
    Transform _shield;
    Renderer _fillRenderer;
    Renderer _shieldRenderer;
    Renderer _backgroundRenderer;
    MaterialPropertyBlock _block;
    Camera _camera;

    void Awake()
    {
        _health = GetComponent<Health>();
        _block = new MaterialPropertyBlock();

        var material = ResolveMaterial();
        if (material == null)
        {
            enabled = false;
            return;
        }

        _root = new GameObject(RootName).transform;
        _root.SetParent(transform, false);
        _root.localPosition = Vector3.up * heightOffset;

        _backgroundRenderer = MakeQuad("Background", material, size * 1.08f, 0f, out _);
        _fillRenderer = MakeQuad("Fill", material, size, -0.001f, out _fill);
        _shieldRenderer = MakeQuad("Shield", material, size, -0.002f, out _shield);

        SetColor(_backgroundRenderer, backgroundColor);
    }

    void OnEnable()
    {
        if (_health != null) _health.Changed += OnHealthChanged;
    }

    void OnDisable()
    {
        if (_health != null) _health.Changed -= OnHealthChanged;
    }

    void Start()
    {
        _camera = Camera.main;
        Refresh();
    }

    void OnHealthChanged(Health health) => Refresh();

    void LateUpdate()
    {
        if (_root == null) return;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        bool visible = ShouldShow();
        if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (!visible) return;

        // Face the camera plane rather than the camera's position: pointing each bar
        // at the eye makes the row of them fan outwards at the screen edges.
        _root.rotation = _camera.transform.rotation;

        // Parent scale would squash the bar on a resized archetype, so undo it.
        Vector3 parentScale = transform.lossyScale;
        _root.localScale = new Vector3(
            SafeInverse(parentScale.x), SafeInverse(parentScale.y), SafeInverse(parentScale.z));

        _root.localPosition = Vector3.up * (heightOffset / Mathf.Max(0.0001f, parentScale.y));
    }

    static float SafeInverse(float value) => Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;

    bool ShouldShow()
    {
        if (_health == null || _health.IsDead) return false;

        if (_camera != null && maxVisibleDistance > 0f &&
            (transform.position - _camera.transform.position).sqrMagnitude >
            maxVisibleDistance * maxVisibleDistance)
            return false;

        if (alwaysVisible) return true;
        return !hideWhenFull || !_health.IsFull;
    }

    void Refresh()
    {
        if (_fill == null || _health == null) return;

        float health01 = _health.Normalized;
        SetFill(_fill, _fillRenderer, health01, HealthColor(health01));

        bool hasShield = _health.maxShield > 0f;
        if (_shield != null) _shield.gameObject.SetActive(hasShield && _health.Shield > 0f);

        if (hasShield) SetFill(_shield, _shieldRenderer, _health.ShieldNormalized, shieldColor);
    }

    Color HealthColor(float normalized)
        => normalized > 0.5f
            ? Color.Lerp(hurtColor, healthyColor, (normalized - 0.5f) * 2f)
            : Color.Lerp(criticalColor, hurtColor, normalized * 2f);

    /// <summary>
    /// Scales the quad down and slides it left by half of what it lost, so the bar
    /// drains from the right instead of shrinking toward its middle.
    /// </summary>
    void SetFill(Transform quad, Renderer renderer, float amount, Color color)
    {
        amount = Mathf.Clamp01(amount);

        quad.localScale = new Vector3(size.x * amount, size.y, 1f);
        quad.localPosition = new Vector3(-size.x * (1f - amount) * 0.5f, 0f, quad.localPosition.z);

        SetColor(renderer, color);
    }

    void SetColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;

        renderer.GetPropertyBlock(_block);
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);
        renderer.SetPropertyBlock(_block);
    }

    Renderer MakeQuad(string name, Material material, Vector2 quadSize, float z, out Transform quad)
    {
        // Built from a mesh rather than CreatePrimitive: a primitive arrives with a
        // collider, and Destroy only takes effect at the end of the frame -- for that
        // frame the bar would be a shootable, Enemy-layered surface floating in mid-air.
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = SharedQuad();

        quad = go.transform;
        quad.SetParent(_root, false);
        quad.localPosition = new Vector3(0f, 0f, z);
        quad.localScale = new Vector3(quadSize.x, quadSize.y, 1f);

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return renderer;
    }

    static Mesh _quadMesh;

    /// <summary>
    /// Both cached objects are created at runtime and destroyed when play mode ends,
    /// but the statics holding them survive because domain reload is disabled. Clearing
    /// them here keeps the next session from reasoning about destroyed assets.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        _quadMesh = null;
        _fallbackMaterial = null;
    }

    /// <summary>One unit quad centred on its origin, shared by every bar in the scene.</summary>
    static Mesh SharedQuad()
    {
        if (_quadMesh != null) return _quadMesh;

        _quadMesh = new Mesh
        {
            name = "HealthBarQuad",
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };

        _quadMesh.RecalculateNormals();
        _quadMesh.RecalculateBounds();
        return _quadMesh;
    }

    /// <summary>
    /// True when a renderer belongs to a health bar rather than to the body. Archetype
    /// tinting and the attack telegraph both sweep an enemy's renderers, and neither
    /// has any business repainting the bar floating above it.
    /// </summary>
    public static bool IsBarRenderer(Renderer renderer)
    {
        if (renderer == null) return false;

        for (var t = renderer.transform; t != null; t = t.parent)
            if (t.name == RootName) return true;

        return false;
    }

    /// <summary>
    /// Prefers the assigned asset. The runtime fallback walks the pipeline's unlit
    /// shaders in turn because Shader.Find only sees what a build actually shipped.
    /// </summary>
    Material ResolveMaterial()
    {
        if (barMaterial != null) return barMaterial;
        if (_fallbackMaterial != null) return _fallbackMaterial;

        string[] candidates =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default"
        };

        foreach (var shaderName in candidates)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) continue;

            // Cached, not per enemy. Every bar draws through one shared material and
            // gets its colour from a property block, so a wave of forty enemies is
            // forty property blocks rather than forty materials.
            _fallbackMaterial = new Material(shader) { name = "HealthBar (runtime)" };
            return _fallbackMaterial;
        }

        Debug.LogWarning($"[EnemyHealthBar] No unlit shader available on {name}; " +
                         "assign Bar Material to draw health bars in a build.", this);
        return null;
    }

    static Material _fallbackMaterial;
}
