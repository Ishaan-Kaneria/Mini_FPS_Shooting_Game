using UnityEngine;

/// <summary>
/// The look of a round striking something (FPSImpactManager): the marks it leaves, the dust or sparks it throws, how long
/// a mark stays and how many there can be. Loaded from Resources; FPSKitImpactAssets makes it with its generated
/// textures and materials (and never overwrites the numbers, so tuning here survives a rebuild).
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Impact Settings", fileName = "ImpactSettings")]
public class ImpactSettings : ScriptableObject
{
    public const string ResourcePath = "ImpactSettings";

    [Header("Materials (made by FPSKitImpactAssets)")]
    [Tooltip("The bullet marks: a 2 x 2 atlas -- concrete hole, metal dent, dark scorch, a second concrete hole -- drawn with vertex colour so a mark can fade.")]
    public Material decalMaterial;
    [Tooltip("Dust and chips: a soft lit puff.")]
    public Material dustMaterial;
    [Tooltip("Sparks: additive, stretched along their flight.")]
    public Material sparkMaterial;

    [Header("Marks")]
    [Tooltip("How many marks can be on the walls at once; the oldest goes when a new one needs its place.")]
    [Range(16, 512)] public int maxDecals = 192;
    [Tooltip("Seconds a mark stays, its fading included.")]
    public float decalLifetime = 30f;
    [Tooltip("Seconds over which a mark fades out at the end of its life.")]
    public float decalFade = 3f;
    [Tooltip("How far a mark sits off the surface, in metres (against flicker).")]
    public float decalOffset = 0.004f;
    [Tooltip("Width of a hole in concrete or stone, in metres (each mark varies 25% about it).")]
    public float concreteHoleSize = 0.09f;
    [Tooltip("Width of a dent in metal, in metres.")]
    public float metalDentSize = 0.065f;
    [Tooltip("Width of the dark mark on anything else, in metres.")]
    public float genericMarkSize = 0.075f;

    [Header("Dust and chips")]
    [Tooltip("Concrete and stone dust: grey-beige.")]
    public Color concreteDust = new Color(0.70f, 0.66f, 0.60f, 0.75f);
    [Tooltip("Sand thrown up from the ground.")]
    public Color sandDust = new Color(0.80f, 0.70f, 0.52f, 0.8f);
    [Tooltip("Wood: splinter-coloured dust.")]
    public Color woodDust = new Color(0.55f, 0.42f, 0.30f, 0.7f);
    [Tooltip("Anything else.")]
    public Color genericDust = new Color(0.55f, 0.54f, 0.52f, 0.6f);
    [Tooltip("Snow and ice.")]
    public Color snowDust = new Color(0.92f, 0.95f, 1f, 0.8f);
    [Tooltip("Puffs of dust per strike into concrete.")]
    public int concreteDustCount = 12;
    [Tooltip("Seconds a puff of dust hangs before it is gone.")]
    public Vector2 dustLifetime = new Vector2(0.9f, 1.7f);

    [Header("Sparks")]
    [Tooltip("Hot metal: bright orange, well over white so it blooms.")]
    [ColorUsage(false, true)] public Color sparkColor = new Color(4.5f, 1.8f, 0.45f, 1f);
    [Tooltip("Sparks per strike into metal.")]
    public int sparkCount = 18;
    [Tooltip("How fast they leave the surface, metres a second.")]
    public Vector2 sparkSpeed = new Vector2(5f, 11f);
    [Tooltip("The flash of light with the sparks: how bright, how far, how long (no shadows).")]
    public float flashIntensity = 5f;
    public float flashRange = 2.4f;
    public float flashDuration = 0.03f;

    static ImpactSettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    public static ImpactSettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<ImpactSettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<ImpactSettings>();
                _active.name = "Default Impacts";
            }
            return _active;
        }
    }
}
