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

    // ==================================================================
    // Open zone
    // ==================================================================
    /// <summary>
    /// How the deadly water reads. Only the dressing changes -- every one of these is
    /// the same gorge with the same trigger at the bottom of it, because the thing the
    /// player has to learn is "that is the edge and the bridge is the way over", and
    /// they should only have to learn it once.
    /// </summary>
    public enum Hazard
    {
        River,
        /// <summary>Ice over water. The gap is the part that has already broken.</summary>
        Ice,
        /// <summary>A dry drop. No surface at the bottom, just the fall.</summary>
        Chasm,
        Lava,
        /// <summary>Flooded or live rail. Shallow, so it burns rather than drowns.</summary>
        Electrified
    }

    [Header("Open Zone")]
    [Tooltip("Build this arena as open ground with a horizon instead of as a walled box. " +
             "Everything under this heading is ignored when it is off, and the arena is " +
             "built the old way -- a floor, four perimeter walls and scattered cover.")]
    public bool openZone;

    [Tooltip("Metres the ground runs past the playable edge before the backdrop takes " +
             "over. This is the difference between standing in a level and standing in a " +
             "place: the eye needs something between the last wall and the sky.")]
    [Min(0f)] public float apronSize = 900f;

    [Tooltip("What the water is. Dressing only -- the gorge and the kill trigger in the " +
             "bottom of it are the same whichever this is.")]
    public Hazard hazard = Hazard.River;

    [Tooltip("Metres across. This is the length of the bridge, and the bridge is the " +
             "most dangerous ground in the level, so it is really a question about how " +
             "long the player is out in the open.")]
    [Min(4f)] public float hazardWidth = 55f;

    [Tooltip("Metres from the rim down to the water. Deep enough that falling in reads " +
             "as a mistake rather than as a shortcut.")]
    [Min(2f)] public float hazardDepth = 20f;

    [Tooltip("Metres east of the middle the gorge runs. Off-centre on purpose: the " +
             "player starts at the origin, so a gorge through the middle would start " +
             "them on the rim of it with nowhere to back up to.")]
    public float hazardOffset = 90f;

    [Tooltip("How far the gorge wanders as it crosses the map, in metres. A channel " +
             "ruled dead straight is a channel nobody believes water cut.")]
    [Min(0f)] public float hazardMeander = 26f;

    public Color hazardColor = new Color(0.20f, 0.42f, 0.52f);
    public Color bankColor = new Color(0.44f, 0.38f, 0.28f);

    [Tooltip("Width of the bridge deck. Wide enough for two people to pass and narrow " +
             "enough to be a decision.")]
    [Min(2f)] public float bridgeWidth = 9f;

    [Tooltip("Number of crossings. One is a choke and a story; two is an option and a " +
             "flank. Above two the gorge stops being an obstacle at all.")]
    [Range(1, 3)] public int bridgeCount = 2;

    public Color bridgeColor = new Color(0.46f, 0.42f, 0.36f);

    [Header("Open Zone: Backdrop")]
    [Tooltip("Silhouettes ringed around the level far past the boundary -- mesas, " +
             "ridges, a skyline. No colliders, and off the navigation bake.")]
    [Min(0)] public int backdropCount = 34;

    [Tooltip("Metres out. Far enough that it never reads as somewhere you could walk to.")]
    public Vector2 backdropDistance = new Vector2(620f, 1150f);

    public Vector2 backdropHeight = new Vector2(55f, 190f);
    public Vector2 backdropWidth = new Vector2(70f, 260f);
    public Color backdropColor = new Color(0.44f, 0.36f, 0.28f);

    [Tooltip("Second, nearer band of the same thing, on the ground and solid. These are " +
             "what the player navigates by -- the big rock you go round to get to the " +
             "bridge.")]
    [Min(0)] public int landmarkCount = 9;

    [Header("Open Zone: Content")]
    [Tooltip("Walled compounds with a way in. The strongpoints -- they are what the open " +
             "ground is open between, and what makes crossing it a choice.")]
    [Min(0)] public int outpostCount = 7;

    [Tooltip("Raised decks with a ramp and a lip to shoot over, placed to overlook the " +
             "crossings and the compounds rather than dropped at random.")]
    [Min(0)] public int vantageCount = 8;

    [Tooltip("Rows of chest-high barriers laid across the approaches, so there is a way " +
             "to cross open ground that is not just running at it.")]
    [Min(0)] public int coverLineCount = 14;

    [Tooltip("Clusters of rock or rubble scattered between everything else. The filler " +
             "that stops the ground between two compounds being a killing field.")]
    [Min(0)] public int scatterClusterCount = 26;

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
