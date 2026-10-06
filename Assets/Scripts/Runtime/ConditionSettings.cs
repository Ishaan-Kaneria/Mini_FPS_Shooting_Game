using UnityEngine;

/// <summary>
/// How the player's injuries show without a health bar (see <see cref="PlayerCondition"/>).
/// Loaded from <c>Resources/ConditionSettings</c>; the defaults below stand in when there is none.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Condition Settings", fileName = "ConditionSettings")]
public class ConditionSettings : ScriptableObject
{
    [Header("When it shows")]
    [Tooltip("Share of health lost before anything shows. Below this the player is fine.")]
    [Range(0f, 0.9f)] public float showFrom = 0.25f;
    [Tooltip("Share of health lost from which it is critical: colour drains, the world muffles, the legs go.")]
    [Range(0.1f, 0.95f)] public float criticalFrom = 0.6f;

    [Header("Screen")]
    public Color edgeColour = new Color(0.32f, 0.02f, 0.02f);
    [Tooltip("How far the dark red edges close in at the worst, 0 to 1.")]
    [Range(0f, 1f)] public float edgeAtWorst = 0.55f;
    [Tooltip("How hard a single hit kicks the edges.")]
    [Range(0f, 1f)] public float edgeOnHit = 0.3f;
    [Tooltip("Colour drained at the worst, in saturation points (100 = grey).")]
    [Range(0f, 100f)] public float drainAtWorst = 70f;
    [Tooltip("Stops of exposure lost at the worst.")]
    [Range(0f, 2f)] public float dimAtWorst = 0.45f;
    [Tooltip("Colour split on a hit.")]
    [Range(0f, 1f)] public float splitOnHit = 0.6f;

    [Header("Sound")]
    public AudioClip breathing;
    public AudioClip heartbeat;
    [Range(0f, 1f)] public float breathVolume = 0.55f;
    [Range(0f, 1f)] public float heartVolume = 0.7f;
    [Tooltip("Low-pass cutoff on everything else at the worst, in hertz. 22000 is no muffle.")]
    [Range(300f, 22000f)] public float muffleAtWorst = 1100f;

    [Header("Body")]
    [Tooltip("Movement speed at the worst, as a share of normal.")]
    [Range(0.3f, 1f)] public float speedAtWorst = 0.78f;

    // ======================================================================

    public const string ResourcePath = "ConditionSettings";
    static ConditionSettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    public static ConditionSettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<ConditionSettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<ConditionSettings>();
                _active.name = "Default Condition";
            }
            return _active;
        }
    }
}
