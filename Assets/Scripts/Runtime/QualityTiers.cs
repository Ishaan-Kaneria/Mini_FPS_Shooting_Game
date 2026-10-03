using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Picks the quality tier, the frame rate and the resolution policy, and applies them to
/// each scene as it loads.
///
/// <b>The tier is chosen once, on the first launch, and then belongs to the player.</b> A
/// phone or a browser starts on Low and a desktop on High; the choice is written to
/// <see cref="GameSettings.QualityTier"/>, so a player who moved it is never moved back.
/// The tiers themselves -- what Low turns off -- are pipeline assets built by
/// <c>FPSKitQualityTiers</c>; this only selects between them, plus the two things that
/// belong to a scene rather than to the pipeline:
///
///   * <b>post-processing</b>, a per-camera switch, off on Low and in battery saver;
///   * <b>particles</b>, halved on Low. Snow, dust and smoke are the arena's largest
///     overdraw, and at half the count they still read as weather.
///
/// <b>Fog is left alone on every tier</b>, deliberately. It is not what costs frames -- it
/// is a term in the shader the lighting pays anyway -- and it is what hides the far clip
/// plane on arenas four and five hundred metres across. Thinning it on the tier with the
/// shortest draw distance would show the player exactly where the world stops.
///
/// <b>Frame rate.</b> A desktop paces to its display with vsync. A phone runs at the
/// player's 30 or 60, and battery saver pins it at 30. A browser is paced by the page and
/// is left alone. On a phone the render resolution also follows the frame time
/// (<see cref="DynamicResolution"/>), so a heavy moment costs sharpness instead of frames.
/// </summary>
public static class QualityTiers
{
    static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_hooked)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameSettings.Changed -= OnSettingChanged;
        }
        _hooked = false;
        _builtScale = null;
    }

    /// <summary>A phone, a tablet, or a browser: the platforms Low is the default for.</summary>
    public static bool Handheld => Application.isMobilePlatform || WebDevice.IsTouchOnly;

    static bool Web => Application.platform == RuntimePlatform.WebGLPlayer;

    /// <summary>
    /// The graphics chip shares the CPU's memory and power budget: an Intel or AMD integrated part. Judged from the
    /// device name, because Unity reports no "is integrated" flag.
    /// Apple silicon is unified-memory but fast enough for High, so it is not counted. Intel's discrete Arc cards are
    /// named "Arc A770" / "Arc B580" (a letter and three digits); the integrated part is just "Arc Graphics".
    /// </summary>
    public static bool IntegratedGpu
    {
        get
        {
            string n = SystemInfo.graphicsDeviceName.ToLowerInvariant();
            if (n.Contains("apple")) return false;
            if (n.Contains("intel")) return !System.Text.RegularExpressions.Regex.IsMatch(n, @"arc\W*(tm)?\W*[ab]\d{3}");
            if (n.Contains("radeon"))
                return !n.Contains(" rx ") && !n.Contains("pro w") && !n.Contains("pro v")
                       && System.Text.RegularExpressions.Regex.IsMatch(n, @"(radeon\W*(tm)?\W*graphics|radeon\W*\d{3}m|vega\W*\d+)");
            return false;
        }
    }

    /// <summary>
    /// The tier a first launch starts on: Low on a phone or browser, Medium on integrated graphics, High on a
    /// dedicated card. Only the first launch asks; after that the player's choice stands.
    /// </summary>
    public static GameSettings.Quality DefaultTier
        => Handheld || Web ? GameSettings.Quality.Low
         : IntegratedGpu ? GameSettings.Quality.Medium
         : GameSettings.Quality.High;

    public static GameSettings.Quality Current
        => (GameSettings.Quality)(GameSettings.QualityTier < 0 ? (int)DefaultTier : GameSettings.QualityTier);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        // Not in the editor: SetQualityLevel there writes the project's current level to
        // disk as a side effect of pressing Play.
        if (!Application.isEditor && GameSettings.QualityTier < 0)
        {
            GameSettings.QualityTier = (int)DefaultTier;
            Debug.Log($"[Quality] first launch on \"{SystemInfo.graphicsDeviceName}\" ({(IntegratedGpu ? "integrated" : "dedicated")}): {DefaultTier}");
        }

        ApplyTier();
        ApplyFrameRate();
        if (!_hooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameSettings.Changed += OnSettingChanged;
            _hooked = true;
        }
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyScene();

    static void OnSettingChanged(string key)
    {
        // A new tier brings its own render scale; the slider's value belonged to the old one.
        if (key == "quality") GameSettings.ForgetRenderScale();
        if (key is "quality" or "renderScale" or "*") ApplyTier();
        if (key is "fps" or "battery" or "*") ApplyFrameRate();
        if (key is "quality" or "battery" or "*") ApplyScene();
    }

    // Each pipeline asset's own scale, taken the first time the asset is seen: the slider writes to the asset in memory,
    // so reading it later would return the slider's value, not the tier's.
    static System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, float> _builtScale;

    static float BuiltScale(UniversalRenderPipelineAsset urp)
    {
        if (urp == null) return 1f;
        _builtScale ??= new System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, float>();
        if (!_builtScale.TryGetValue(urp, out float s)) _builtScale[urp] = s = urp.renderScale;
        return s;
    }

    /// <summary>The scale the current tier itself asks for (its asset as built), before any slider.</summary>
    public static float TierRenderScale => BuiltScale(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset);

    static void ApplyRenderScale()
    {
        // A pipeline asset edited while the editor plays stays edited on disk; the slider is for builds.
        if (Application.isEditor) return;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return;
        float built = BuiltScale(urp);
        float wanted = GameSettings.RenderScale;
        urp.renderScale = wanted > 0f ? wanted : built;
    }

    static void ApplyTier()
    {
        if (Application.isEditor) return;
        string wanted = Current.ToString();
        var names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            if (!string.Equals(names[i], wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
            // True: swap the pipeline asset now, not next frame, or the first scene of a
            // session renders through the tier it is leaving.
            if (i != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(i, true);
            ApplyRenderScale();
            return;
        }
    }

    public static void ApplyFrameRate()
    {
        if (Web) return;
        if (Handheld)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = GameSettings.BatterySaver ? 30 : GameSettings.FrameRate;
        }
        else
        {
            // The player's choice; off, the frame rate is whatever the machine gives.
            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            Application.targetFrameRate = -1;
        }
    }

    /// <summary>Post-processing, particles and dynamic resolution, for what is loaded now.</summary>
    public static void ApplyScene()
    {
        bool low = Current == GameSettings.Quality.Low;
        bool post = !low && !GameSettings.BatterySaver;

        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && data.renderType == CameraRenderType.Base) data.renderPostProcessing = post;
        }

        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
        {
            var cut = ps.GetComponent<ParticleBudget>();
            if (cut == null) cut = ps.gameObject.AddComponent<ParticleBudget>();
            cut.Apply(low ? 0.5f : 1f);
        }

        if (Handheld && Camera.main != null && Camera.main.GetComponent<DynamicResolution>() == null)
            Camera.main.gameObject.AddComponent<DynamicResolution>();
    }
}

/// <summary>
/// Remembers a particle system's authored budget so a tier can scale it without compounding.
/// </summary>
[DisallowMultipleComponent]
public class ParticleBudget : MonoBehaviour
{
    [SerializeField] int _maxParticles = -1;
    [SerializeField] float _rate = -1f;

    public void Apply(float fraction)
    {
        var ps = GetComponent<ParticleSystem>();
        if (ps == null) return;
        var main = ps.main;
        var emission = ps.emission;
        if (_maxParticles < 0)
        {
            _maxParticles = main.maxParticles;
            _rate = emission.rateOverTimeMultiplier;
        }
        main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(_maxParticles * fraction));
        emission.rateOverTimeMultiplier = _rate * fraction;
    }
}
