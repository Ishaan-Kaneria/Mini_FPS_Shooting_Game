using UnityEngine;

/// <summary>
/// Everything blood is drawn with: the spray and mist particle systems, and the
/// materials for splats on the world, wounds on a body and the pool under a corpse.
///
/// An asset rather than something found at runtime for the reason every generated
/// material in the kit is: a shader nothing references is stripped from the build, and
/// blood that works in the editor and is invisible in the browser is the worst kind of
/// bug to find. The enemy prefab points here, so a build carries all of it.
///
/// Built by <c>FPSKitGore</c>. The textures are grayscale shapes; the colour is on the
/// materials, as everywhere else in the kit.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Blood Library", fileName = "Blood")]
public class BloodLibrary : ScriptableObject
{
    [Header("Particles")]
    [Tooltip("Droplets thrown out of an exit wound. Emitted into by code, never played.")]
    public ParticleSystem sprayPrefab;

    [Tooltip("The fine red cloud at the wound itself.")]
    public ParticleSystem mistPrefab;

    [Header("Materials")]
    [Tooltip("Splats on walls and floors. A 2x2 atlas: each splat picks a quarter.")]
    public Material splatMaterial;

    [Tooltip("The spreading pool under a body. One round shape, not an atlas.")]
    public Material poolMaterial;

    [Tooltip("A wound on the body itself: a dark hole with a torn red rim.")]
    public Material woundMaterial;

    [Header("Colour")]
    [Tooltip("What a droplet is tinted. Dark: fresh blood in daylight is nearly black-red.")]
    public Color dropColor = new Color(0.34f, 0.015f, 0.02f, 1f);

    [Tooltip("The mist at a wound. A little brighter, because it is thin and backlit.")]
    public Color mistColor = new Color(0.55f, 0.04f, 0.04f, 0.7f);

    [Tooltip("What a hit throws up with blood switched off in Settings: a dull puff that " +
             "still says the round landed.")]
    public Color bloodlessColor = new Color(0.32f, 0.3f, 0.28f, 0.55f);

    [Tooltip("What a stained body part is darkened toward as it is shot up.")]
    public Color stainColor = new Color(0.2f, 0.02f, 0.02f, 1f);

    [Header("Budget")]
    [Tooltip("Splats kept in the world at once, per quality tier (Low, Medium, High). " +
             "The oldest is reused past this, so a long fight never costs more.")]
    public Vector3Int decalBudget = new Vector3Int(48, 96, 160);

    [Tooltip("Droplets per hit at full severity, before the quality tier scales it.")]
    [Min(1)] public int dropsPerHit = 26;
}
