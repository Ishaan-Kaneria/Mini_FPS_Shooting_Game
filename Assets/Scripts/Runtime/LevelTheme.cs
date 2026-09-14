using UnityEngine;

/// <summary>
/// A complete look for one arena: sky, sun, fog, ambient light, material palette
/// and prop rules. FPSKitSceneBuilder reads one of these to build a scene, so a
/// new theme is a new asset rather than new code.
///
/// Everything here also works at runtime, so you can apply a theme live.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Level Theme", fileName = "Theme")]
public class LevelTheme : ScriptableObject
{
    [Header("Identity")]
    public string themeName = "Industrial";
    [TextArea(2, 4)] public string description;

    [Header("Sky")]
    [Tooltip("Leave empty to generate a procedural sky from the tints below.")]
    public Material skyboxOverride;
    [ColorUsage(false, true)] public Color skyTint = new Color(0.5f, 0.55f, 0.62f);
    public Color skyGroundColor = new Color(0.28f, 0.26f, 0.24f);
    [Range(0f, 5f)] public float atmosphereThickness = 1f;
    [Range(0f, 8f)] public float skyExposure = 1.1f;

    [Header("Sun")]
    public Color sunColor = new Color(1f, 0.96f, 0.89f);
    public float sunIntensity = 1.2f;
    [Tooltip("x = pitch (elevation), y = yaw (compass direction).")]
    public Vector2 sunAngles = new Vector2(48f, 32f);
    [Range(0f, 1f)] public float shadowStrength = 0.85f;

    [Header("Fog")]
    public bool fogEnabled = true;
    public Color fogColor = new Color(0.5f, 0.53f, 0.58f);
    [Range(0f, 0.1f)] public float fogDensity = 0.008f;

    [Header("Ambient Light")]
    public Color ambientSky = new Color(0.45f, 0.5f, 0.6f);
    public Color ambientEquator = new Color(0.3f, 0.3f, 0.33f);
    public Color ambientGround = new Color(0.15f, 0.14f, 0.13f);

    [Header("Arena Shape")]
    [Tooltip("Width of the square arena in metres. 60 is a tight competitive arena; " +
             "110-150 gives you somewhere to actually roam.")]
    public float arenaSize = 110f;
    public float wallHeight = 5f;
    public int randomSeed = 7;

    [Tooltip("Multiply every layout count by the arena's area, so making the map bigger " +
             "does not just make it emptier. Counts below are calibrated for the " +
             "reference size.")]
    public bool scaleLayoutWithArea = true;
    public float layoutReferenceSize = 60f;

    /// <summary>Area ratio against the reference size, used to scale layout counts.</summary>
    public float LayoutScale => !scaleLayoutWithArea || layoutReferenceSize <= 1f
        ? 1f
        : Mathf.Clamp(arenaSize * arenaSize / (layoutReferenceSize * layoutReferenceSize), 0.25f, 8f);

    public int ScaledCount(int baseCount) => Mathf.Max(0, Mathf.RoundToInt(baseCount * LayoutScale));

    [Header("Arena Layout")]
    [Tooltip("Roofless buildings with doorways. The backbone of the level.")]
    public int roomCount = 3;

    [Tooltip("Raised decks with ramps. Verticality is what stops an arena feeling flat.")]
    public int platformCount = 2;

    [Tooltip("Chest-high walls that create firing lanes.")]
    public int coverWallCount = 7;

    [Tooltip("Clusters of stacked crates. Tagged Wood.")]
    public int crateStackCount = 7;

    [Tooltip("Tall thin columns that break up sightlines.")]
    public int pillarCount = 5;

    public Vector2 coverHeightRange = new Vector2(1.1f, 1.5f);
    public Vector2 coverWidthRange = new Vector2(4f, 9f);

    [Header("Material Palette")]
    public Color floorColor = new Color(0.34f, 0.34f, 0.36f);
    public Color wallColor = new Color(0.5f, 0.5f, 0.52f);
    public Color[] coverColors = { new Color(0.4f, 0.42f, 0.45f) };
    [Range(0f, 1f)] public float floorSmoothness = 0.2f;
    [Range(0f, 1f)] public float wallSmoothness = 0.15f;
    [Range(0f, 1f)] public float coverMetallic = 0f;

    [Tooltip("Surface tag applied to cover blocks. Drives impact effects.")]
    public string floorTag = "Concrete";
    public string wallTag = "Concrete";
    public string coverTag = "Metal";

    [Header("Accent Lights")]
    public int accentLightCount = 0;
    public Color accentLightColor = new Color(1f, 0.6f, 0.25f);
    public float accentLightIntensity = 8f;
    public float accentLightRange = 14f;
    public float accentLightHeight = 3.5f;

    [Header("Surface Materials (drag in art-pack materials)")]
    [Tooltip("Leave empty for flat colour. Assign a tiling material and the builder makes a " +
             "copy with the tiling corrected for each surface's real size.")]
    public Material floorMaterial;

    [Tooltip("Metres covered by one repeat of the texture. Smaller = more detail, more repetition.")]
    public float floorTextureSize = 4f;

    public Material wallMaterial;
    public float wallTextureSize = 4f;

    [Tooltip("Cover walls, platforms and pillars. Falls back to wallMaterial, then flat colour.")]
    public Material coverMaterial;

    [Tooltip("Crate stacks. Falls back to flat colour.")]
    public Material crateMaterial;

    [Header("Optional Content")]
    [Tooltip("Real props from an art pack. Scattered across the arena alongside the greybox layout.")]
    public GameObject[] propPrefabs;

    [Tooltip("How many props to try to place. Raise this if the arena still looks bare.")]
    public int propCount = 40;

    [Tooltip("Particle system for snow, dust, rain. Spawned above the arena.")]
    public GameObject weatherPrefab;

    public AudioClip ambienceLoop;
    [Range(0f, 1f)] public float ambienceVolume = 0.35f;

    [Header("Post Processing")]
    public bool enablePostProcessing = true;
    [Range(0f, 2f)] public float bloomIntensity = 0.45f;
    [Range(-2f, 2f)] public float postExposure = 0f;
    [Range(-100f, 100f)] public float saturation = 0f;
    [Range(-100f, 100f)] public float contrast = 8f;
    public Color colorFilter = Color.white;
    [Range(0f, 1f)] public float vignetteIntensity = 0.28f;
    [Range(0f, 1f)] public float filmGrain = 0.2f;

    public Color RandomCoverColor(System.Random rng)
    {
        if (coverColors == null || coverColors.Length == 0) return wallColor;
        return coverColors[rng.Next(coverColors.Length)];
    }

    /// <summary>Applies sky, fog, ambient and sun settings. Safe to call at runtime.</summary>
    public void ApplyEnvironment(Light sun, Material generatedSkybox = null)
    {
        Material sky = skyboxOverride != null ? skyboxOverride : generatedSkybox;
        if (sky != null) RenderSettings.skybox = sky;

        RenderSettings.fog = fogEnabled;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;

        if (sun != null)
        {
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadowStrength = shadowStrength;
            sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, 0f);
            RenderSettings.sun = sun;
        }

        DynamicGI.UpdateEnvironment();
    }
}
