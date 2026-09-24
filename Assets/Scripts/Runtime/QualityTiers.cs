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
    }

    /// <summary>A phone, a tablet, or a browser: the platforms Low is the default for.</summary>
    public static bool Handheld => Application.isMobilePlatform || WebDevice.IsTouchOnly;

    static bool Web => Application.platform == RuntimePlatform.WebGLPlayer;

    /// <summary>The tier a first launch starts on.</summary>
    public static GameSettings.Quality DefaultTier
        => Handheld || Web ? GameSettings.Quality.Low : GameSettings.Quality.High;

    public static GameSettings.Quality Current
        => (GameSettings.Quality)(GameSettings.QualityTier < 0 ? (int)DefaultTier : GameSettings.QualityTier);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        // Not in the editor: SetQualityLevel there writes the project's current level to
        // disk as a side effect of pressing Play.
        if (!Application.isEditor && GameSettings.QualityTier < 0)
            GameSettings.QualityTier = (int)DefaultTier;

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
        if (key is "quality" or "*") ApplyTier();
        if (key is "fps" or "battery" or "*") ApplyFrameRate();
        if (key is "quality" or "battery" or "*") ApplyScene();
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
            return;
        }
    }

    static void ApplyFrameRate()
    {
        if (Web) return;
        if (Handheld)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = GameSettings.BatterySaver ? 30 : GameSettings.FrameRate;
        }
        else
        {
            QualitySettings.vSyncCount = 1;
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
